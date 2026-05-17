# Vitriol .NET 10 Port

A faithful .NET 10 re-architecture of the Python Vitriol conversion engine, scoped to a class library plus CLI front-end.

This is an **in-progress port**. See the design document at [`../docs/dotnet10-lossless-file-conversion-plan.md`](../docs/dotnet10-lossless-file-conversion-plan.md) for the full migration plan.

## Status

| Sprint | Scope | Status |
|---|---|---|
| 0 | Repo scaffolding | done |
| 1 | `Vitriol.Core` foundation (IRs, interfaces, adapters) | done |
| 2 | Detection + Router | done |
| 3 | Stone envelope + crypto + TXT/ZIP/PNG hosts | partial — UCMSv1 + TXT/ZIP/PNG-v1 done; PNG-v3 Mandelbrot deferred |
| 4 | Round-trip verifier | not started |
| 5 | First IR handler (Text) + `Vitriol.Cli` | not started |
| 6+ | Image / Doc / Media / 3D / Stone audio + video / Bootstrap | deferred |

## Build

Requires the **.NET 10 SDK** (preview channel). Install via [dot.net](https://dot.net) or `dotnet-install.sh -Channel 10.0 -Quality preview`.

```bash
cd dotnet
dotnet restore Vitriol.sln
dotnet build Vitriol.sln
dotnet test Vitriol.sln
```

## Run (once Sprint 5 lands)

```bash
dotnet run --project Vitriol.Cli -- convert <src> <dst> [--verify] [--password <p>] [--masquerade]
```

## Project layout

```
dotnet/
├── Vitriol.sln                 # Solution
├── Directory.Build.props       # net10.0, nullable, warnings-as-errors, invariant globalization
├── Directory.Packages.props    # Central package management
├── .editorconfig               # C# style + analyzer severity
├── Vitriol.Core/               # IRs, abstractions, router, detection, registry, 7 routing gates
├── Vitriol.Stone/              # UCMSv1/v3 envelope, AES-256-CTR, TXT/ZIP/PNG-v1 hosts
├── Vitriol.Formats.Text/       # Sprint 5   — first IR handler (not yet present)
├── Vitriol.Cli/                # Sprint 5   — System.CommandLine front-end (not yet present)
└── Vitriol.Tests/              # Sprint 1+ — xUnit + FsCheck + golden files (not yet present)
```

## License

Elastic License — see [`../LICENSE`](../LICENSE).

## Cross-references

- Design + losslessness analysis: [`../docs/dotnet10-lossless-file-conversion-plan.md`](../docs/dotnet10-lossless-file-conversion-plan.md)
- Python implementation (canonical reference): [`../app/`](../app/)
- Round-trip fixtures: [`../samples/`](../samples/)
