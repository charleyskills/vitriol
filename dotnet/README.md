# Vitriol .NET 10 Port

A faithful .NET 10 re-architecture of the Python Vitriol conversion engine, scoped to a class library plus CLI front-end.

This is an **in-progress port**. See the design document at [`../docs/dotnet10-lossless-file-conversion-plan.md`](../docs/dotnet10-lossless-file-conversion-plan.md) for the full migration plan.

## Status

| Sprint | Scope | Status |
|---|---|---|
| 0 | Repo scaffolding | done |
| 1 | `Vitriol.Core` foundation (IRs, interfaces, adapters) | done |
| 2 | Detection + Router | done |
| 3 | Stone envelope + crypto + TXT/ZIP/PNG hosts | done (UCMSv1 + UCMSv3 + TXT/ZIP/PNG-v1; PNG-v3 added in Sprint 10) |
| 4 | Round-trip verifier | done |
| 5 | First IR handler (Text) + `Vitriol.Cli` | done |
| 6 | Stone audio v1 hosts (WAV + AIFF) | done |
| 7 | Image handler (`Vitriol.Formats.Image` via SixLabors.ImageSharp) | done |
| 8 | Tabular handler (`Vitriol.Formats.Tabular`: CSV/TSV via CsvHelper + XLSX via ClosedXML) | done |
| 9 | Archive handler (`Vitriol.Formats.Archive` via SharpCompress: ZIP/TAR family read+write, 7Z/RAR read-only) | done |
| 10 | Stone PNG v3 (Mandelbrot fractal carrier + UCMSv3 LSB scatter-pack) | done |
| 11 | Crypto handler (`Vitriol.Formats.Crypto`: PEM ↔ CRT ↔ CER ↔ KEY ↔ DER via BCL `System.Security.Cryptography`) | done |
| 12 | Doc handler (`Vitriol.Formats.Doc`: DOCX via OpenXml + PDF via PdfPig/QuestPDF + Markdown via Markdig); trailer-envelope sidecar lights up the Sprint 2 `TrailerEnvelopeGate` for the first time | done |
| 13+ | EPUB / ODT / PPTX / RTF / Media subprocess / 3D / Bootstrap | deferred |

## Build

Requires the **.NET 10 SDK** (preview channel). Install via [dot.net](https://dot.net) or `dotnet-install.sh -Channel 10.0 -Quality preview`.

```bash
cd dotnet
dotnet restore Vitriol.slnx
dotnet build Vitriol.slnx
dotnet test Vitriol.slnx
```

The solution uses the XML-based **`.slnx`** format (introduced in .NET 9, fully supported in .NET 10) — no GUIDs, just project paths.

## Run

> **Running the compiled binary vs. `dotnet run`**
>
> The examples below use `dotnet run --project Vitriol.Cli -- convert …`.
> The bare `--` is a *dotnet run* separator — it tells the `dotnet` CLI that everything
> after it belongs to the app, not to dotnet itself. It is **not** part of the subcommand name.
>
> When invoking the published `vitriol.exe` (or `vitriol` on Unix) directly, drop both
> `dotnet run --project Vitriol.Cli` and the `--`:
>
> ```
> # compiled binary
> .\vitriol.exe convert src.7z dst.png --masquerade --password chopin --verify
>
> # dev / source tree (dotnet run)
> dotnet run --project Vitriol.Cli -- convert src.7z dst.png --masquerade --password chopin --verify
> #                                ^^  dotnet separator — NOT part of the subcommand name
> ```

```bash
dotnet run --project Vitriol.Cli -- convert <src> <dst> [--verify] [--password <p>] [--masquerade] [--compiler] [--verbose]
```

Examples:

```bash
# Image transcoding via SixLabors.ImageSharp (same-handler media path)
dotnet run --project Vitriol.Cli -- convert photo.png photo.jpg
dotnet run --project Vitriol.Cli -- convert photo.png photo.webp

# Tabular conversions (single-sheet round trip + multi-sheet drop warning)
dotnet run --project Vitriol.Cli -- convert data.csv data.xlsx
dotnet run --project Vitriol.Cli -- convert workbook.xlsx out.csv   # warns about dropped sheets

# Cross-kind: CSV → text-flavoured destination engages the tabular→textdoc adapter
dotnet run --project Vitriol.Cli -- convert sales.csv summary.html

# Archive conversions (cross-format repack + same-kind byte passthrough via .cbz alias)
dotnet run --project Vitriol.Cli -- convert backup.cbz backup.tar.gz
dotnet run --project Vitriol.Cli -- convert legacy.7z legacy.zip   # 7Z read-only; cross-kind only

# X.509 certificate / key format conversions
dotnet run --project Vitriol.Cli -- convert server.pem server.crt    # PEM cert → DER cert
dotnet run --project Vitriol.Cli -- convert key.der key.pem          # DER key → PEM PKCS#8

# Document conversions
dotnet run --project Vitriol.Cli -- convert notes.docx notes.md       # DOCX → Markdown
dotnet run --project Vitriol.Cli -- convert paper.pdf paper.md        # PDF → Markdown (warns on scanned/empty PDFs)
dotnet run --project Vitriol.Cli -- convert article.md article.docx   # Markdown → DOCX

# A 7-Zip archive hidden inside a Mandelbrot fractal PNG
# Vitriol treats the .7z as opaque bytes — it never opens the archive and
# DOES NOT need the 7z's own internal password.
dotnet run --project Vitriol.Cli -- convert secrets.7z hidden.png --masquerade --verify
dotnet run --project Vitriol.Cli -- convert hidden.png recovered.7z --masquerade --verify
# When the source 7z is ALREADY encrypted (7z a -p"…" secrets.7z …), the above
# is sufficient — 7-Zip's internal AES-256 keeps the contents safe.

# OPTIONAL second encryption layer via Vitriol's own --password (Stone v3
# AES-256-CTR, PBKDF2-derived). Useful when the source is plaintext, or as
# belt-and-suspenders on top of an already-encrypted 7z.
dotnet run --project Vitriol.Cli -- convert plaintext.bin hidden.png --password chopin --verify
dotnet run --project Vitriol.Cli -- convert hidden.png recovered.bin --password chopin --verify
# Wrong --password produces silent garbage (no oracle), so don't lose it.
# --password implies --masquerade.

# Defensive: refuse to produce a Stone carrier without a password. Useful
# when scripting a "always encrypt" workflow — guards against accidentally
# omitting --password and silently emitting a plaintext UCMSv1 envelope.
dotnet run --project Vitriol.Cli -- convert plaintext.bin hidden.png --require-password --password chopin --verify
# (omitting --password with --require-password set exits with usage error)

# THE headline new property: trailer-envelope byte-perfect round trip
dotnet run --project Vitriol.Cli -- convert photo.png photo.docx --verify
# Forward writes a DOCX containing photo's bytes stashed at _vitriol/original.bin.
# Reverse reads the trailer, recovers photo bytes exactly. --verify confirms.

# Plain-text pass-through with byte-perfect whitespace preservation
dotnet run --project Vitriol.Cli -- convert in.txt out.log

# Philosopher's Stone: embed a file inside a ZIP host and verify the round-trip
dotnet run --project Vitriol.Cli -- convert secret.bin carrier.zip --masquerade --verify

# Same but inside a PNG (UCMSv1 ucMs chunk)
dotnet run --project Vitriol.Cli -- convert secret.bin carrier.png --masquerade --verify

# AES-256-encrypted Stone v3: encrypted UCMSv3 envelope scatter-packed into a real
# Mandelbrot fractal. The PNG opens in any viewer as a recognizable fractal image.
dotnet run --project Vitriol.Cli -- convert secret.bin carrier.png --masquerade --password chopin --verify

vitriol --help
vitriol convert --help
```

Exit codes: `0` success · `2` usage · `64` unsupported conversion · `65` verification failed · `70` internal error.

### Why Vitriol never asks for your 7z's password

Vitriol treats `.7z` (and every other source format) as an **opaque byte
stream**. It does not open the archive, does not read its central
directory, does not decrypt anything inside. So the password you set
with `7z a -p"…"` stays inside the `.7z` file — Vitriol never asks for
it and you should never pass it via `--password`.

### Two independent layers (optional)

There are up to two encryption layers in play, and they do not interact:

1. **The 7-Zip archive's own AES-256** (optional, applied by 7-Zip when
   you create the archive with `-p`). Protects the *files inside* the
   archive.
2. **Vitriol's Stone v3 AES-256-CTR** (optional, applied by Vitriol when
   you pass `--password`). PBKDF2-HMAC-SHA256 (200,000 iterations)
   derives a key and encrypts the UCMSv3 envelope that wraps the `.7z`
   bytes before the LSB scatter-pack into the Mandelbrot pixels.

If your 7z is already encrypted, layer 2 is redundant — omit
`--password`. If your source is plaintext and you want protection,
layer 2 is the easy way to get AES-256 without re-archiving.

Either password can be forgotten without affecting the other layer's
correctness, but losing the **only** password layer in use makes the
contents unrecoverable.

Vitriol's `--password` follows a **no-oracle policy**: the wrong
password produces silent garbage bytes rather than an error. The
recovered `.7z` file simply won't open in any 7z client. There is no
way to tell "wrong password" from "right password but corrupted
carrier" — that's by design (`docs/dotnet10-lossless-file-conversion-plan.md`
Section 4).

## Project layout

```
dotnet/
├── Vitriol.slnx                # Solution (XML-based .slnx format)
├── Directory.Build.props       # net10.0, nullable, warnings-as-errors, invariant globalization
├── Directory.Packages.props    # Central package management
├── .editorconfig               # C# style + analyzer severity
├── Vitriol.Core/               # IRs, abstractions, router, detection, registry, 10 routing gates, verifier
├── Vitriol.Stone/              # UCMSv1/v3 envelope, AES-256-CTR, TXT/ZIP/WAV-v1/AIFF-v1 hosts plus PNG v1+v3 (Mandelbrot LSB scatter-pack)
├── Vitriol.Formats.Text/       # PlainTextHandler — txt/log/py/xml/html read+write+stream
├── Vitriol.Formats.Image/      # ImageMediaHandler — png/jpg/webp/bmp/tiff/gif/pbm/tga via ImageSharp
├── Vitriol.Formats.Tabular/    # CsvTextHandler (.csv, .tsv) + XlsxHandler (.xlsx) via CsvHelper + ClosedXML
├── Vitriol.Formats.Archive/    # ArchiveHandler — zip/cbz/7z/cb7/tar/tar.gz/tar.bz2/tar.xz/tar.zst/rar via SharpCompress
├── Vitriol.Formats.Crypto/     # CryptoHandler — pem/crt/cer/key/der via BCL System.Security.Cryptography
├── Vitriol.Formats.Doc/        # DocxHandler + PdfHandler + MarkdownHandler with trailer-envelope sidecar
│                               # (OpenXml + PdfPig/QuestPDF + Markdig)
├── Vitriol.Cli/                # vitriol convert <src> <dst> hand-rolled arg parser, DI host
└── Vitriol.Tests/              # xUnit + FsCheck + Shouldly — ~80 tests
```

## License

Elastic License — see [`../LICENSE`](../LICENSE).

## Cross-references

- Design + losslessness analysis: [`../docs/dotnet10-lossless-file-conversion-plan.md`](../docs/dotnet10-lossless-file-conversion-plan.md)
- Python implementation (canonical reference): [`../app/`](../app/)
- Round-trip fixtures: [`../samples/`](../samples/)
