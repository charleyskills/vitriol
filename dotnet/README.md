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
| 11+ | Doc / Media subprocess / 3D / Stone audio-v3 music synth + video / Bootstrap | deferred |

## Build

Requires the **.NET 10 SDK** (preview channel). Install via [dot.net](https://dot.net) or `dotnet-install.sh -Channel 10.0 -Quality preview`.

```bash
cd dotnet
dotnet restore Vitriol.sln
dotnet build Vitriol.sln
dotnet test Vitriol.sln
```

## Run

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

## Project layout

```
dotnet/
├── Vitriol.sln                 # Solution
├── Directory.Build.props       # net10.0, nullable, warnings-as-errors, invariant globalization
├── Directory.Packages.props    # Central package management
├── .editorconfig               # C# style + analyzer severity
├── Vitriol.Core/               # IRs, abstractions, router, detection, registry, 10 routing gates, verifier
├── Vitriol.Stone/              # UCMSv1/v3 envelope, AES-256-CTR, TXT/ZIP/WAV-v1/AIFF-v1 hosts plus PNG v1+v3 (Mandelbrot LSB scatter-pack)
├── Vitriol.Formats.Text/       # PlainTextHandler — txt/log/py/xml/html read+write+stream
├── Vitriol.Formats.Image/      # ImageMediaHandler — png/jpg/webp/bmp/tiff/gif/pbm/tga via ImageSharp
├── Vitriol.Formats.Tabular/    # CsvTextHandler (.csv, .tsv) + XlsxHandler (.xlsx) via CsvHelper + ClosedXML
├── Vitriol.Formats.Archive/    # ArchiveHandler — zip/cbz/7z/cb7/tar/tar.gz/tar.bz2/tar.xz/tar.zst/rar via SharpCompress
├── Vitriol.Cli/                # vitriol convert <src> <dst> hand-rolled arg parser, DI host
└── Vitriol.Tests/              # xUnit + FsCheck + Shouldly — ~80 tests
```

## License

Elastic License — see [`../LICENSE`](../LICENSE).

## Cross-references

- Design + losslessness analysis: [`../docs/dotnet10-lossless-file-conversion-plan.md`](../docs/dotnet10-lossless-file-conversion-plan.md)
- Python implementation (canonical reference): [`../app/`](../app/)
- Round-trip fixtures: [`../samples/`](../samples/)
