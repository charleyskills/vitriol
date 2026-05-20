using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vitriol.Core.Collections;
using Vitriol.Core.Ir;
using Vitriol.Core.Pipeline;

namespace Vitriol.Formats.Crypto;

/// <summary>
/// X.509 cert and private/public-key conversions: PEM ↔ CRT/CER (DER cert)
/// ↔ KEY (DER key, PKCS#8) ↔ DER. Mirrors
/// <c>app/format_handlers/crypto_handler.py</c>.
///
/// <para><b>Read</b>: sniffs <c>BEGIN</c> markers for PEM; otherwise tries
/// DER cert / PKCS#8 private key / SubjectPublicKeyInfo public key in turn
/// based on extension hint then unconditionally.</para>
///
/// <para><b>Write</b>: emits PEM for <c>.pem</c>, raw DER for <c>.crt</c> /
/// <c>.cer</c> (first cert) / <c>.key</c> (private key) / <c>.der</c> (first
/// of cert > key > pubkey).</para>
///
/// <para><b>Out of scope</b>: encrypted private keys (no password UX),
/// PKCS#7 / PKCS#12 bundles. Matches Vitriol's v1 scope.</para>
/// </summary>
public sealed class CryptoHandler : IFormatReader, IFormatWriter
{
    public DocKind Kind => DocKind.Crypto;

    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pem", ".crt", ".cer", ".key", ".der",
        };

    public async ValueTask<IDocument> ReadAsync(Stream input, ReadContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        using MemoryStream buf = new();
        await input.CopyToAsync(buf, cancellationToken).ConfigureAwait(false);
        byte[] raw = buf.ToArray();

        return LooksLikePem(raw) ? ParsePem(raw) : ParseDer(raw, context.Extension);
    }

    public ValueTask WriteAsync(IDocument document, Stream output, WriteContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        CryptoDoc doc = document switch
        {
            CryptoDoc c => c,
            _ => throw new InvalidOperationException(
                $"CryptoHandler.Write expects a CryptoDoc, got {document.GetType().Name}."),
        };

        byte[] payload = context.Extension.ToLowerInvariant() switch
        {
            ".pem" => EmitPem(doc),
            ".crt" or ".cer" => FirstCertDerOrThrow(doc),
            ".key" => FirstKeyDerOrThrow(doc),
            ".der" => FirstAnyDerOrThrow(doc),
            _ => throw new UnsupportedConversionException(
                $"Unsupported crypto target: {context.Extension}"),
        };

        return output.WriteAsync(payload, cancellationToken);
    }

    // -- PEM helpers ---------------------------------------------------------

    private static bool LooksLikePem(byte[] raw)
    {
        int searchLen = Math.Min(raw.Length, 2048);
        ReadOnlySpan<byte> needle = "-----BEGIN "u8;
        return raw.AsSpan(0, searchLen).IndexOf(needle) >= 0;
    }

    private static CryptoDoc ParsePem(byte[] raw)
    {
        string text = Encoding.ASCII.GetString(raw);
        ReadOnlySpan<char> remaining = text.AsSpan();
        ImmutableArray<ReadOnlyByteArray>.Builder certs = ImmutableArray.CreateBuilder<ReadOnlyByteArray>();
        ReadOnlyByteArray? keyDer = null;
        ReadOnlyByteArray? pubKeyDer = null;

        while (PemEncoding.TryFind(remaining, out PemFields fields))
        {
            string label = remaining[fields.Label].ToString();
            ReadOnlySpan<char> base64 = remaining[fields.Base64Data];
            byte[] der = Convert.FromBase64String(base64.ToString());

            if (label.EndsWith("CERTIFICATE", StringComparison.Ordinal) && !label.Contains("REQUEST", StringComparison.Ordinal))
            {
                // X509Certificate2 normalizes DER on round trip; rely on the constructor
                // to validate and then re-export the canonical DER.
                using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(der);
                certs.Add(new ReadOnlyByteArray(cert.Export(X509ContentType.Cert)));
            }
            else if (label.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                keyDer = new ReadOnlyByteArray(LoadAnyPrivateKeyToPkcs8Der(der, label));
            }
            else if (label.Contains("PUBLIC KEY", StringComparison.Ordinal))
            {
                pubKeyDer = new ReadOnlyByteArray(LoadAnyPublicKeyToSpkiDer(der));
            }
            // Other labels (DH PARAMETERS, EC PARAMETERS, certificate requests, etc.)
            // are silently dropped — matches Python crypto_handler.py:131-132.

            // Advance past this PEM block.
            remaining = remaining[fields.Location.End..];
        }

        CryptoDoc result = new(
            new EquatableArray<ReadOnlyByteArray>(certs.ToImmutable()),
            keyDer,
            pubKeyDer,
            DocumentMetadata.Empty);

        if (result.IsEmpty)
        {
            throw new InvalidDataException(
                "PEM source contained no recognized cert / private key / public key blocks.");
        }
        return result;
    }

    private static byte[] EmitPem(CryptoDoc doc)
    {
        if (doc.IsEmpty)
        {
            throw new InvalidOperationException("Empty CryptoDoc — nothing to emit as PEM.");
        }

        StringBuilder sb = new();
        foreach (ReadOnlyByteArray certDer in doc.CertsDer)
        {
            using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(certDer.ToArray());
            sb.Append(cert.ExportCertificatePem());
            sb.Append('\n');
        }
        if (doc.KeyDer is not null)
        {
            sb.Append(PrivateKeyPkcs8DerToPem(doc.KeyDer.ToArray()));
            sb.Append('\n');
        }
        if (doc.PublicKeyDer is not null)
        {
            sb.Append(PublicKeySpkiDerToPem(doc.PublicKeyDer.ToArray()));
            sb.Append('\n');
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] FirstCertDerOrThrow(CryptoDoc doc)
    {
        if (doc.CertsDer.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot write .crt/.cer: source contains no X.509 certificate.");
        }
        return doc.CertsDer[0].ToArray();
    }

    private static byte[] FirstKeyDerOrThrow(CryptoDoc doc)
    {
        if (doc.KeyDer is null)
        {
            throw new InvalidOperationException(
                "Cannot write .key: source contains no private key.");
        }
        return doc.KeyDer.ToArray();
    }

    private static byte[] FirstAnyDerOrThrow(CryptoDoc doc)
    {
        if (doc.CertsDer.Count > 0)
        {
            return doc.CertsDer[0].ToArray();
        }
        if (doc.KeyDer is not null)
        {
            return doc.KeyDer.ToArray();
        }
        if (doc.PublicKeyDer is not null)
        {
            return doc.PublicKeyDer.ToArray();
        }
        throw new InvalidOperationException("Empty CryptoDoc — nothing to write as .der.");
    }

    // -- DER helpers ---------------------------------------------------------

    private static CryptoDoc ParseDer(byte[] raw, string extension)
    {
        // Try the most likely format first based on extension, then fall back.
        // Matches the precedence in Python crypto_handler.py:168-223.

        if (extension is ".crt" or ".cer" or ".der")
        {
            if (TryLoadDerCert(raw, out byte[]? certDer))
            {
                return new CryptoDoc(
                    new EquatableArray<ReadOnlyByteArray>(
                        ImmutableArray.Create(new ReadOnlyByteArray(certDer!))),
                    KeyDer: null,
                    PublicKeyDer: null,
                    Metadata: DocumentMetadata.Empty);
            }
        }

        if (extension is ".key" or ".der")
        {
            if (TryLoadDerPrivateKey(raw, out byte[]? keyDer))
            {
                return new CryptoDoc(
                    CertsDer: EquatableArray<ReadOnlyByteArray>.Empty,
                    KeyDer: new ReadOnlyByteArray(keyDer!),
                    PublicKeyDer: null,
                    Metadata: DocumentMetadata.Empty);
            }
        }

        if (TryLoadDerPublicKey(raw, out byte[]? pubDer))
        {
            return new CryptoDoc(
                CertsDer: EquatableArray<ReadOnlyByteArray>.Empty,
                KeyDer: null,
                PublicKeyDer: new ReadOnlyByteArray(pubDer!),
                Metadata: DocumentMetadata.Empty);
        }

        // Fallbacks not gated on extension.
        if (TryLoadDerCert(raw, out byte[]? lateCertDer))
        {
            return new CryptoDoc(
                new EquatableArray<ReadOnlyByteArray>(
                    ImmutableArray.Create(new ReadOnlyByteArray(lateCertDer!))),
                KeyDer: null,
                PublicKeyDer: null,
                Metadata: DocumentMetadata.Empty);
        }
        if (TryLoadDerPrivateKey(raw, out byte[]? lateKeyDer))
        {
            return new CryptoDoc(
                CertsDer: EquatableArray<ReadOnlyByteArray>.Empty,
                KeyDer: new ReadOnlyByteArray(lateKeyDer!),
                PublicKeyDer: null,
                Metadata: DocumentMetadata.Empty);
        }

        throw new InvalidDataException(
            $"Could not parse {extension} as DER cert / private key / public key. "
            + "If this is encrypted, decrypt it first.");
    }

    private static bool TryLoadDerCert(byte[] raw, out byte[]? canonicalDer)
    {
        try
        {
            using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(raw);
            canonicalDer = cert.Export(X509ContentType.Cert);
            return true;
        }
        catch (CryptographicException)
        {
            canonicalDer = null;
            return false;
        }
    }

    private static bool TryLoadDerPrivateKey(byte[] raw, out byte[]? pkcs8Der)
    {
        // Try RSA first (most common), then ECDsa, then DSA.
        foreach (Func<AsymmetricAlgorithm> creator in new Func<AsymmetricAlgorithm>[]
        {
            RSA.Create,
            ECDsa.Create,
            DSA.Create,
        })
        {
            try
            {
                using AsymmetricAlgorithm key = creator();
                key.ImportPkcs8PrivateKey(raw, out _);
                pkcs8Der = key.ExportPkcs8PrivateKey();
                return true;
            }
            catch (CryptographicException) { /* try next */ }
        }
        pkcs8Der = null;
        return false;
    }

    private static bool TryLoadDerPublicKey(byte[] raw, out byte[]? spkiDer)
    {
        foreach (Func<AsymmetricAlgorithm> creator in new Func<AsymmetricAlgorithm>[]
        {
            RSA.Create,
            ECDsa.Create,
            DSA.Create,
        })
        {
            try
            {
                using AsymmetricAlgorithm key = creator();
                key.ImportSubjectPublicKeyInfo(raw, out _);
                spkiDer = key.ExportSubjectPublicKeyInfo();
                return true;
            }
            catch (CryptographicException) { /* try next */ }
        }
        spkiDer = null;
        return false;
    }

    private static byte[] LoadAnyPrivateKeyToPkcs8Der(byte[] der, string pemLabel)
    {
        // PKCS#8 PEM ("PRIVATE KEY") wraps the algorithm OID inside the DER,
        // so all three impls accept it without further hints. Legacy
        // PKCS#1 RSA ("RSA PRIVATE KEY") and SEC1 EC ("EC PRIVATE KEY") are
        // narrower; .NET's RSA / ECDsa ImportFromPem handles them when fed
        // the original PEM. For simplicity, treat all as PKCS#8 first and
        // fall back via ImportFromPem on the original DER bytes.
        Exception? lastError = null;
        foreach (Func<AsymmetricAlgorithm> creator in new Func<AsymmetricAlgorithm>[]
        {
            RSA.Create,
            ECDsa.Create,
            DSA.Create,
        })
        {
            try
            {
                using AsymmetricAlgorithm key = creator();
                key.ImportPkcs8PrivateKey(der, out _);
                return key.ExportPkcs8PrivateKey();
            }
            catch (CryptographicException e)
            {
                lastError = e;
            }
        }
        // Legacy formats (PKCS#1 RSA, SEC1 EC) require their dedicated import:
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(der, out _);
            return rsa.ExportPkcs8PrivateKey();
        }
        catch (CryptographicException) { /* not RSA PKCS#1 */ }
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportECPrivateKey(der, out _);
            return ec.ExportPkcs8PrivateKey();
        }
        catch (CryptographicException) { /* not SEC1 EC */ }

        throw new InvalidDataException(
            $"PEM private key block '{pemLabel}' did not parse as RSA / ECDsa / DSA in PKCS#8, PKCS#1, or SEC1.",
            lastError);
    }

    private static byte[] LoadAnyPublicKeyToSpkiDer(byte[] der)
    {
        Exception? lastError = null;
        foreach (Func<AsymmetricAlgorithm> creator in new Func<AsymmetricAlgorithm>[]
        {
            RSA.Create,
            ECDsa.Create,
            DSA.Create,
        })
        {
            try
            {
                using AsymmetricAlgorithm key = creator();
                key.ImportSubjectPublicKeyInfo(der, out _);
                return key.ExportSubjectPublicKeyInfo();
            }
            catch (CryptographicException e)
            {
                lastError = e;
            }
        }
        throw new InvalidDataException(
            "PEM public key block did not parse as RSA / ECDsa / DSA SubjectPublicKeyInfo.",
            lastError);
    }

    private static string PrivateKeyPkcs8DerToPem(byte[] pkcs8Der)
    {
        Exception? lastError = null;
        foreach (Func<AsymmetricAlgorithm> creator in new Func<AsymmetricAlgorithm>[]
        {
            RSA.Create,
            ECDsa.Create,
            DSA.Create,
        })
        {
            try
            {
                using AsymmetricAlgorithm key = creator();
                key.ImportPkcs8PrivateKey(pkcs8Der, out _);
                return key.ExportPkcs8PrivateKeyPem();
            }
            catch (CryptographicException e)
            {
                lastError = e;
            }
        }
        throw new InvalidDataException(
            "Private key DER did not load as RSA / ECDsa / DSA PKCS#8.",
            lastError);
    }

    private static string PublicKeySpkiDerToPem(byte[] spkiDer)
    {
        Exception? lastError = null;
        foreach (Func<AsymmetricAlgorithm> creator in new Func<AsymmetricAlgorithm>[]
        {
            RSA.Create,
            ECDsa.Create,
            DSA.Create,
        })
        {
            try
            {
                using AsymmetricAlgorithm key = creator();
                key.ImportSubjectPublicKeyInfo(spkiDer, out _);
                return key.ExportSubjectPublicKeyInfoPem();
            }
            catch (CryptographicException e)
            {
                lastError = e;
            }
        }
        throw new InvalidDataException(
            "Public key DER did not load as RSA / ECDsa / DSA SubjectPublicKeyInfo.",
            lastError);
    }
}
