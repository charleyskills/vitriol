using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vitriol.Core.Pipeline;
using Vitriol.Formats.Crypto;

namespace Vitriol.Tests.Formats.Crypto;

public sealed class CryptoHandlerTests
{
    private readonly CryptoHandler _handler = new();

    [Fact]
    public void Reports_crypto_kind_and_extension_set()
    {
        _handler.Kind.ShouldBe(DocKind.Crypto);
        _handler.SupportedExtensions.ShouldContain(".pem");
        _handler.SupportedExtensions.ShouldContain(".crt");
        _handler.SupportedExtensions.ShouldContain(".cer");
        _handler.SupportedExtensions.ShouldContain(".key");
        _handler.SupportedExtensions.ShouldContain(".der");
    }

    [Fact]
    public async Task Rsa_self_signed_cert_pem_to_crt_round_trip()
    {
        (byte[] certDer, _) = MakeSelfSignedRsaCert("CN=Vitriol Test");
        string pem = CertDerToPem(certDer);

        // PEM → CryptoDoc → CRT (DER) → CryptoDoc — bytes must match.
        IDocument fromPem = await _handler.ReadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes(pem)),
            new ReadContext(".pem"), default);
        CryptoDoc certs = fromPem.ShouldBeOfType<CryptoDoc>();
        certs.CertsDer.Count.ShouldBe(1);
        certs.KeyDer.ShouldBeNull();

        await using MemoryStream crt = new();
        await _handler.WriteAsync(certs, crt, new WriteContext(".crt"), default);
        crt.ToArray().ShouldBe(certs.CertsDer[0].ToArray());

        // Now CRT → PEM and verify the cert is still recognized.
        crt.Position = 0;
        IDocument fromCrt = await _handler.ReadAsync(crt, new ReadContext(".crt"), default);
        CryptoDoc roundTrip = fromCrt.ShouldBeOfType<CryptoDoc>();
        roundTrip.CertsDer.Count.ShouldBe(1);
        roundTrip.CertsDer[0].ToArray().ShouldBe(certs.CertsDer[0].ToArray());
    }

    [Fact]
    public async Task Rsa_private_key_pem_to_key_to_pem_round_trip()
    {
        (_, RSA rsa) = MakeSelfSignedRsaCert("CN=Vitriol Test");
        try
        {
            string pem = rsa.ExportPkcs8PrivateKeyPem();

            CryptoDoc keyDoc = (CryptoDoc)await _handler.ReadAsync(
                new MemoryStream(Encoding.ASCII.GetBytes(pem)),
                new ReadContext(".pem"), default);
            keyDoc.KeyDer.ShouldNotBeNull();
            keyDoc.CertsDer.Count.ShouldBe(0);

            // PEM → .key (DER PKCS#8) → PEM
            await using MemoryStream derStream = new();
            await _handler.WriteAsync(keyDoc, derStream, new WriteContext(".key"), default);

            derStream.Position = 0;
            CryptoDoc fromKey = (CryptoDoc)await _handler.ReadAsync(derStream, new ReadContext(".key"), default);
            fromKey.KeyDer.ShouldNotBeNull();
            fromKey.KeyDer!.ToArray().ShouldBe(keyDoc.KeyDer!.ToArray());

            // PEM emission yields a recognizable PEM string with PRIVATE KEY label.
            await using MemoryStream pemStream = new();
            await _handler.WriteAsync(fromKey, pemStream, new WriteContext(".pem"), default);
            string emittedPem = Encoding.ASCII.GetString(pemStream.ToArray());
            emittedPem.ShouldContain("BEGIN PRIVATE KEY");
            emittedPem.ShouldContain("END PRIVATE KEY");
        }
        finally
        {
            rsa.Dispose();
        }
    }

    [Fact]
    public async Task Public_key_pem_to_der_round_trip()
    {
        using RSA rsa = RSA.Create(2048);
        string pem = rsa.ExportSubjectPublicKeyInfoPem();

        CryptoDoc fromPem = (CryptoDoc)await _handler.ReadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes(pem)),
            new ReadContext(".pem"), default);
        fromPem.PublicKeyDer.ShouldNotBeNull();
        fromPem.CertsDer.Count.ShouldBe(0);
        fromPem.KeyDer.ShouldBeNull();

        // .der path picks publickey when nothing else is present.
        await using MemoryStream der = new();
        await _handler.WriteAsync(fromPem, der, new WriteContext(".der"), default);
        der.ToArray().ShouldBe(fromPem.PublicKeyDer!.ToArray());
    }

    [Fact]
    public async Task Pem_bundle_with_cert_and_key_keeps_both()
    {
        (byte[] certDer, RSA rsa) = MakeSelfSignedRsaCert("CN=Bundle Test");
        try
        {
            string bundlePem = CertDerToPem(certDer) + "\n" + rsa.ExportPkcs8PrivateKeyPem();

            CryptoDoc doc = (CryptoDoc)await _handler.ReadAsync(
                new MemoryStream(Encoding.ASCII.GetBytes(bundlePem)),
                new ReadContext(".pem"), default);

            doc.CertsDer.Count.ShouldBe(1);
            doc.KeyDer.ShouldNotBeNull();

            // .crt write should emit cert only.
            await using MemoryStream crt = new();
            await _handler.WriteAsync(doc, crt, new WriteContext(".crt"), default);
            crt.ToArray().ShouldBe(doc.CertsDer[0].ToArray());

            // .key write should emit key only.
            await using MemoryStream key = new();
            await _handler.WriteAsync(doc, key, new WriteContext(".key"), default);
            key.ToArray().ShouldBe(doc.KeyDer!.ToArray());
        }
        finally
        {
            rsa.Dispose();
        }
    }

    [Fact]
    public async Task Empty_pem_input_rejected()
    {
        byte[] noBlocks = Encoding.ASCII.GetBytes("Just some text with no PEM blocks at all.\n");
        await Should.ThrowAsync<InvalidDataException>(async () =>
            await _handler.ReadAsync(new MemoryStream(noBlocks), new ReadContext(".pem"), default));
    }

    [Fact]
    public async Task Malformed_der_rejected_with_clear_error()
    {
        byte[] garbage = Enumerable.Range(0, 64).Select(i => (byte)(i * 7)).ToArray();
        await Should.ThrowAsync<InvalidDataException>(async () =>
            await _handler.ReadAsync(new MemoryStream(garbage), new ReadContext(".crt"), default));
    }

    [Fact]
    public async Task Write_rejects_crt_target_with_no_cert_in_doc()
    {
        using RSA rsa = RSA.Create(2048);
        string keyPem = rsa.ExportPkcs8PrivateKeyPem();
        CryptoDoc keyOnly = (CryptoDoc)await _handler.ReadAsync(
            new MemoryStream(Encoding.ASCII.GetBytes(keyPem)),
            new ReadContext(".pem"), default);

        await using MemoryStream dst = new();
        InvalidOperationException ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(keyOnly, dst, new WriteContext(".crt"), default));
        ex.Message.ShouldContain("no X.509 certificate");
    }

    [Fact]
    public async Task Write_rejects_key_target_with_no_key_in_doc()
    {
        (byte[] certDer, _) = MakeSelfSignedRsaCert("CN=Certs Only");
        CryptoDoc certOnly = (CryptoDoc)await _handler.ReadAsync(
            new MemoryStream(certDer), new ReadContext(".crt"), default);

        await using MemoryStream dst = new();
        InvalidOperationException ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(certOnly, dst, new WriteContext(".key"), default));
        ex.Message.ShouldContain("no private key");
    }

    [Fact]
    public async Task Write_rejects_non_crypto_document()
    {
        await using MemoryStream dst = new();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _handler.WriteAsync(Vitriol.Core.Ir.TextDoc.Empty, dst, new WriteContext(".pem"), default));
    }

    [Fact]
    public async Task Cer_alias_works_same_as_crt()
    {
        (byte[] certDer, _) = MakeSelfSignedRsaCert("CN=Alias Test");
        await using MemoryStream src = new(certDer);

        CryptoDoc doc = (CryptoDoc)await _handler.ReadAsync(src, new ReadContext(".cer"), default);

        await using MemoryStream dst = new();
        await _handler.WriteAsync(doc, dst, new WriteContext(".cer"), default);
        dst.ToArray().ShouldBe(certDer);
    }

    /// <summary>
    /// Build a self-signed RSA cert + key for round-trip tests. Uses .NET's
    /// CertificateRequest so the tests don't depend on external test vectors.
    /// </summary>
    private static (byte[] CertDer, RSA Rsa) MakeSelfSignedRsaCert(string subject)
    {
        RSA rsa = RSA.Create(2048);
        try
        {
            CertificateRequest req = new(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(365));
            byte[] der = cert.Export(X509ContentType.Cert);
            return (der, rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static string CertDerToPem(byte[] der)
    {
        using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(der);
        return cert.ExportCertificatePem() + "\n";
    }
}
