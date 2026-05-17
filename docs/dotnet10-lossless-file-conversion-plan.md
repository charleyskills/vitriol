# Vitriol — Existing File-Conversion Mechanism and a .NET 10 C# Re-Architecture Plan

> **Scope of this document.** It does two things:
>
> 1. Documents, with code citations, how the **current Python/PySide6 Vitriol codebase** reads, parses, maps, transforms, serializes, and writes files in both directions.
> 2. Proposes a faithful **.NET 10 C# re-architecture** scoped to a class library plus CLI front-end. GUI port is explicitly deferred.
>
> Where Vitriol is genuinely lossless, the document says so and points at the supporting code. Where the "lossless" framing is misleading, the document says that too — clearly and prominently. Honest engineering verdicts beat marketing copy.

---

## 0. Execution Status

Legend: ✅ done · 🟡 partial · ⏳ deferred

### Sprints delivered (commits on `claude/file-conversion-dotnet10-analysis-bLfwr`)

| Sprint | Scope | Status |
|---|---|---|
| 0 | Repo scaffolding (`dotnet/` solution, Directory.Build.props, central package mgmt, .editorconfig, CI workflow) | ✅ |
| 1 | `Vitriol.Core` foundation — IR records, EquatableArray/EquatableDictionary, all interfaces, IR adapters | ✅ |
| 2 | Detection (`ExtensionNormalizer`, `FormatDetector`, `MagicByteSniffer`, `ZipSubtypeSniffer`) + 10-gate `ConversionRouter` + `FormatRegistry` + `KnownExtensions` | ✅ |
| 3 | `Vitriol.Stone` — UCMSv1 + UCMSv3 envelope, PBKDF2 + AES-256-CTR with deterministic IV, `TxtStoneHost` / `ZipStoneHost` / `PngStoneHost` (v1) | 🟡 PNG-v3 Mandelbrot-XOR carrier deferred |
| 4 | `IRoundTripVerifier` with SHA-256 byte compare + structural-equivalence fallback, `Sha256` helper, `TempScope`, `IrStructuralEquivalence` | ✅ |
| 5 | `Vitriol.Formats.Text` (`PlainTextHandler` reader+writer+stream-converter, BOM-aware `EncodingDetector` with non-UTF-8 warning) + `Vitriol.Cli` (`convert <src> <dst>` with `--verify`/`--masquerade`/`--password`/`--compiler`) | ✅ |
| 6 | Stone audio v1 hosts — `WavStoneHost` (RIFF data chunk) + `AiffStoneHost` (FORM/SSND with IEEE 754 80-bit extended-float sample rate) | 🟡 v3 music-synth variants deferred |
| 7 | `Vitriol.Formats.Image` — first `IMediaHandler` via SixLabors.ImageSharp; covers PNG/JPG/WebP/BMP/TIFF/GIF/PBM/TGA. Lights up `SameMediaHandlerGate` and `CrossCategoryImageToDocumentGate` (origin sidecar path) | ✅ |
| 8 | `Vitriol.Formats.Tabular` — `CsvTextHandler` (.csv/.tsv via CsvHelper) + `XlsxHandler` (.xlsx via ClosedXML). First `DocKind.Tabular` producers/consumers in the port; `TabularToTextDocAdapter` now fires in production via cross-kind CSV → text/markdown/html | ✅ |
| 9 | `Vitriol.Formats.Archive` — `ArchiveHandler` via SharpCompress. ZIP/CBZ/TAR/TAR.GZ/TAR.BZ2/TAR.XZ read+write with same-kind byte passthrough and cross-kind repack via SharpCompress's `IReader`/`IWriter`. 7Z/CB7, RAR/CBR, TAR.ZST are read-only. First `DocKind.Archive` producer/consumer in the port. Zip-Slip mitigation included | 🟡 7Z write + TAR.ZST write + RAR write deferred (library / format constraints) |
| 10 | Stone PNG v3 — Mandelbrot fractal carrier with UCMSv3 LSB scatter-pack. `MandelbrotViewports` (64 curated viewports), `MandelbrotSeed` (port of `derive_seed`), `MandelbrotGenerator` (float32 iteration + three-sin palette), `MandelbrotDims` (tier table), `MandelbrotBitPack` (golden-ratio scatter coprime stride, MSB-first), `MandelbrotPngCodec` (hand-rolled RGB PNG with all 5 filter types on read). `PngStoneHost` dispatches v1 vs v3 by `options.CrossCategory \|\| !Password.IsEmpty`; v3 extract preserves the no-oracle property on wrong password | 🟡 NumPy pixel-byte parity not guaranteed (visual fractal only); LSB scatter positions match Python exactly so cross-implementation payload extract works. Streaming-strip processing for >50 MB payloads deferred |
| 11 | `Vitriol.Formats.Crypto` — PEM ↔ CRT/CER (DER cert) ↔ KEY (PKCS#8 DER) ↔ DER round trip via BCL `System.Security.Cryptography`. Bundles (cert + key in one PEM) preserved through `CryptoDoc` IR. New `DocKind.Crypto` enum value mirrors Python's isolated `DOC_KIND = "crypto"`. RSA / ECDsa / DSA tried in turn for unknown algorithms; PKCS#1 / SEC1 legacy formats handled. Encrypted private keys out of scope (no password UX) | ✅ |
| 12 | `Vitriol.Formats.Doc` — DOCX read+write via `DocumentFormat.OpenXml`, PDF read via `UglyToad.PdfPig`, PDF write via `QuestPDF` (Community license set at DI time), Markdown read via `Markdig` + hand-rolled emitter with bundle-aware image folder layout. Adds two `ITrailerEnvelopeReader` implementations (DOCX `_vitriol/original.bin` ZIP entry + PDF post-`%%EOF` scan) that **finally** activate Sprint 2's `TrailerEnvelopeGate`. Sprint 1's `_vitriol_origin` sidecar plumbing now lives end-to-end through PNG → DOCX → PNG byte-perfect round trips | 🟡 EPUB / ODT / PPTX / RTF / DejaVu Sans TTF embedding for full-Unicode PDF write / real PDF image embedding still deferred to Sprint 13+ |

### Outstanding deferred work (Sprint 13+, no fixed order)

| Area | Status | Notes |
|---|---|---|
| Stone PNG v3 — NumPy pixel-byte parity + streaming-strip processing | ⏳ | Sprint 10 ships the visual fractal + correct LSB positions; byte-exact match with Python's iteration scheme and the >50 MB streaming path are deferred |
| Stone audio v3 (procedural music synthesis port of `_music.py`, FLAC via FFmpeg, M4A/ALAC via FFmpeg) | ⏳ | Music synth port + FFmpeg subprocess wrapper |
| Stone video v3 (MKV animated Mandelbrot at 30 fps, payload in pixel LSBs) | ⏳ | Highest complexity in the Stone catalogue; needs FFmpeg frame pipe |
| Stone 3D v3 (PLY / OBJ / GLB envelope embedding) | ⏳ | Format-aware byte stuffing; modest scope |
| Self-extracting `.py` / `.exe` Stone outputs | ⏳ | Port of `tools/selfextract_stub.py` + `tools/build_selfextract_stub.py` |
| `Vitriol.Formats.Tabular` Parquet / Feather / ORC (via Apache.Arrow + Parquet.Net) | ⏳ | Columnar binary; first-row-as-header semantics differ from CSV/XLSX. CSV/TSV/XLSX already shipped in Sprint 8 |
| `Vitriol.Formats.Doc` extensions — EPUB, ODT, PPTX, RTF read; DejaVu Sans TTF embedding for PDF; real PDF image embedding | ⏳ | DOCX/PDF/Markdown core + trailer-envelope sidecar already ship in Sprint 12 |
| `Vitriol.Formats.Archive` — 7Z write + TAR.ZST write + RAR write | ⏳ | Library/format constraints prevent these in the .NET port today. ZIP/CBZ/TAR/TAR.GZ/TAR.BZ2/TAR.XZ already ship in Sprint 9; 7Z/RAR read also work |
| `Vitriol.Formats.Model` (3D via AssimpNet wrapping the Assimp DLL) | ⏳ | Needs `Vitriol.Bootstrap` to fetch the native binary |
| `Vitriol.Formats.Media` (audio/video via FFMpegCore subprocess wrapper) | ⏳ | Needs `Vitriol.Bootstrap`; mirrors Vitriol's existing FFmpeg codec map |
| `Vitriol.Formats.Pandoc` (subprocess wrapper for ~50 markup formats) | ⏳ | HTML pivot strategy from Sprint 1 adapter registry |
| `Vitriol.Formats.Font` (OTF/TTF/WOFF/WOFF2 via SixLabors.Fonts) | ⏳ | Cleanly bounded; fonttools port |
| `Vitriol.Formats.Crypto` encrypted-private-key support | ⏳ | Would need a CLI password-prompt UX. Cleartext PEM/DER paths already ship in Sprint 11 |
| Optional image plugins (AVIF / HEIC / JXL via Magick.NET) | ⏳ | Needs the native ImageMagick binary |
| SVG read (Svg.Skia) | ⏳ | Read-only, like Vitriol |
| `Vitriol.Bootstrap` — FFmpeg / Pandoc / Assimp / DejaVu Sans auto-fetch with SHA-256 pinning | ⏳ | Precondition for all subprocess-backed handlers |
| Parallel-run CI harness (compare Python Vitriol vs .NET Vitriol on the same corpus) | ⏳ | Migration plan Phase 9 below |
| NativeAOT single-file publish | ⏳ | Migration plan Phase 10 below |
| GUI port (Avalonia or MAUI) | ⏳ | Explicitly out of MVP scope; follow-on phase 11 |

### Component summary (where the code lives today)

`dotnet/Vitriol.Core/` ✅ — IR, adapters, detection, registry, routing, verification.
`dotnet/Vitriol.Stone/` 🟡 — envelope + crypto + 5 carrier-host extensions (TXT, ZIP, PNG v1+v3, WAV-v1, AIFF-v1). PNG v3 ships the Mandelbrot fractal LSB scatter-pack.
`dotnet/Vitriol.Formats.Text/` ✅ — `PlainTextHandler` for .txt/.log/.py/.xml/.html.
`dotnet/Vitriol.Formats.Image/` ✅ — `ImageMediaHandler` for 8 image format families.
`dotnet/Vitriol.Formats.Tabular/` 🟡 — `CsvTextHandler` (.csv/.tsv) + `XlsxHandler` (.xlsx); Parquet/Feather/ORC deferred.
`dotnet/Vitriol.Formats.Archive/` 🟡 — `ArchiveHandler` covers ZIP/CBZ/TAR/TAR.GZ/TAR.BZ2/TAR.XZ read+write and 7Z/CB7/TAR.ZST/RAR read-only; 7Z/TAR.ZST/RAR write deferred.
`dotnet/Vitriol.Formats.Crypto/` ✅ — `CryptoHandler` for PEM/CRT/CER/KEY/DER via BCL; cleartext only.
`dotnet/Vitriol.Formats.Doc/` 🟡 — `DocxHandler` (OpenXml) + `PdfHandler` (PdfPig read / QuestPDF write) + `MarkdownHandler` (Markdig + bundle layout) + two `ITrailerEnvelopeReader` impls. EPUB / ODT / PPTX / RTF / DejaVu TTF embedding / real PDF image embedding deferred.
`dotnet/Vitriol.Cli/` ✅ — `vitriol convert` end-to-end with verification.
`dotnet/Vitriol.Tests/` ✅ — ~100 xUnit + FsCheck + Shouldly tests covering everything above.

---

## 1. Executive Summary

Vitriol is an offline-first desktop file converter covering ~150 extensions across eleven categories: documents (markup / office / wiki / technical / slides / bibliography), data, images, audio, video, 3D models, archives, comic books, subtitles, fonts, and X.509 crypto material. It is implemented in Python on PySide6 and bundles or auto-fetches three external binaries — FFmpeg (audio/video), Assimp (3D), and Pandoc (~50 markup/document formats) — via `launcher.py`.

A single dispatch function, `convert_file` in `app/core/router.py` (lines 43–312), is the spine of the system. It accepts `(src, dst, src_ext, dst_ext, cancel, progress, masquerade, compiler, password, preserve_animations)` and walks a nine-layer decision tree that selects exactly one of: byte-trailer recovery, self-extracting compile, Philosopher's Stone embed/extract, same-handler media transcode, cross-category image→document, cross-category document→media (auto-Stone), large-file streaming, or the whole-file intermediate-representation (IR) path.

Conversions are triggered either through the GUI (`ConversionQueueModel.add_job` in `app/core/conversion_queue.py:296`) or programmatically. Jobs run in a Qt `QThreadPool` capped at three concurrent workers; each holds a `CancellationToken` (`app/utils/cancellation.py`) checked cooperatively inside handlers.

**Losslessness verdict.** Vitriol is lossless along exactly two well-defined paths:

- **Philosopher's Stone** (`app/format_handlers/masquerade.py`) wraps the source bytes in a `UCMSv1`/`UCMSv2`/`UCMSv3` envelope and embeds that envelope in a carrier (PNG/BMP via Mandelbrot XOR, WAV/AIFF via RIFF/SSND chunks, FLAC/M4A via synthesized music PCM, MKV via animated Mandelbrot frames, ZIP/7Z/TAR as a single member named `original.*`, TXT as base64, PY/EXE as self-extracting stubs). Stone is byte-lossless by construction.
- **Trailer-envelope short-circuit** (`router.py:103–140`) recovers the original source bytes from a Vitriol-written PDF/DOCX/EPUB that still carries its `_vitriol/original.bin` ZIP entry or PDF trailer envelope. Byte-perfect when the trailer survives; survives Vitriol re-reads but not third-party rewrites.

The whole-file **IR path is not lossless**, and the code does not claim otherwise. The `TextDoc` block model in `app/core/intermediate.py:16–74` captures `Run` / `Heading` / `Paragraph` / `List_` / `Table` / `Image` / `CodeBlock` / `HorizontalRule` / `Blockquote` — and nothing else. Fonts, pagination, sections, headers/footers, comments, footnotes, change tracking, OOXML custom XML parts, PDF form fields, EPUB CSS, and most document-level properties are silently dropped. The `textdoc_to_tabular` adapter has an explicit comment "Prose is dropped (lossy)" at `intermediate.py:124`. Image and audio/video paths re-encode through Pillow and FFmpeg respectively, and are not byte-stable even with identical input.

The only correctness guarantee shipped to users is the **Verify Round-Trip** toggle: `conversion_queue.py:156–230` runs the conversion forward into a temp file, then reverse into another temp file, SHA-256 hashes both endpoints with `_sha256_file` at line 233, and commits the output only on byte-exact match. There is **no test suite in the repository** (no `tests/` directory, no `test_*.py`, no `pytest.ini`); verification is entirely user-driven via that toggle and the sample files in `samples/`.

---

## 2. Repository Map

```
vitriol/
├── launcher.py                  # Dependency bootstrap (pip wheels, FFmpeg, Pandoc, Assimp, fonts)
├── main.py                      # PySide6 GUI startup (out of scope for .NET CLI port)
├── theme.qss                    # Qt stylesheet
├── app/
│   ├── core/
│   │   ├── router.py            # convert_file — the single dispatch entry point
│   │   ├── intermediate.py      # TextDoc / Tabular IRs and ADAPTERS registry
│   │   ├── file_detector.py     # Extension + magic-byte format detection
│   │   ├── conversion_queue.py  # QThreadPool job runner + Verify Round-Trip
│   │   ├── config.py            # streaming_threshold(), disk-space helpers
│   │   ├── bundle_writer.py     # Markdown image-bundle output (md/images/)
│   │   ├── estimator.py         # Output-size estimation for UI
│   │   ├── dependency_check.py  # External-binary presence checks
│   │   ├── downloader.py        # HTTPS fetch with progress
│   │   └── updater.py           # In-app version check
│   ├── format_handlers/
│   │   ├── __init__.py          # Handler registry: READERS, WRITERS, MEDIA_HANDLERS,
│   │   │                        #   MEDIA_CATEGORY_OF, STONE_ONLY_SOURCES, AUTO_EXECUTE_EXTS
│   │   ├── masquerade.py        # Philosopher's Stone — UCMSv1/v2/v3 envelopes (6065 LOC)
│   │   ├── _stone_crypto.py     # PBKDF2-HMAC-SHA256 + AES-256-CTR + deterministic IV
│   │   ├── _mandelbrot.py       # Fractal carrier generation (image + video)
│   │   ├── _music.py            # Music-carrier synthesis (audio Stone)
│   │   ├── _pdf_ttf.py          # DejaVu Sans subset embedding for pdf_write
│   │   ├── text_plain.py        # txt, log, py, xml, html, plus CSV/TSV routing
│   │   ├── tabular_text.py      # csv, tsv
│   │   ├── columnar_handler.py  # xlsx, parquet, feather, orc
│   │   ├── image_handler.py     # Pillow + AVIF/HEIF/JXL plugins, SVG read-only
│   │   ├── streaming_image.py   # Tiled image streaming for large inputs
│   │   ├── audio_video.py       # FFmpeg subprocess wrapper
│   │   ├── docx_handler.py      # ZIP+XML re-coded DOCX (no python-docx dep)
│   │   ├── pdf_read.py          # pdfminer.six primary + recoded FlateDecode fallback
│   │   ├── pdf_write.py         # reportlab-based PDF writer, stashes _vitriol_origin
│   │   ├── epub_handler.py      # EPUB read/write
│   │   ├── odt_handler.py       # ODT read-only
│   │   ├── pptx_handler.py      # PPTX read-only
│   │   ├── rtf_handler.py       # RTF read via striprtf
│   │   ├── markdown_parser.py   # Markdown read/write, bundle-aware
│   │   ├── archive_handler.py   # zip, 7z, tar, tar.{gz,bz2,xz,zst}, cbz/cb7
│   │   ├── subtitle_handler.py  # srt, vtt, ass, ssa, sub, mpl
│   │   ├── model_handler.py     # Assimp subprocess wrapper, optional preserve_animations
│   │   ├── font_handler.py      # otf/ttf/woff/woff2 via fonttools
│   │   ├── crypto_handler.py    # X.509 (pem, crt, cer, key, der)
│   │   ├── vcf_handler.py       # vCard
│   │   ├── config_handler.py    # json, jsonl, yaml, toml, ini, env
│   │   ├── charset.py           # Encoding detection helpers
│   │   ├── pandoc_handler.py    # Subprocess wrapper for ~50 markup formats
│   │   └── _vendor/             # Vendored Python deps for portable build
│   ├── ui/                      # PySide6 widgets (out of scope for CLI port)
│   └── utils/
│       ├── cancellation.py      # CancellationToken
│       ├── logger.py            # Rotating file log
│       ├── net.py               # HTTPS helpers
│       ├── paths.py             # Cross-platform appdata
│       └── settings.py          # QSettings wrapper
├── tools/                       # Build scripts (PyInstaller, NSIS, self-extract stubs)
└── samples/                     # Manual round-trip samples (not automated tests)
```

**Notable absence.** There is no `tests/` directory. `find . -name 'test_*.py' -o -name 'tests' -type d` returns nothing. The only artifacts that exercise correctness are the files under `samples/` and the runtime Verify Round-Trip flag.

---

## 3. Existing Conversion Mechanism

### 3.1 Job creation

The UI builds a `Job` dataclass (`conversion_queue.py:42–55`) carrying `id`, `src`, `dst`, `src_ext`, `dst_ext`, `save_over_original`, `masquerade`, `verify_round_trip`, `compiler`, `password`, `preserve_animations`, `total_bytes`, and a `CancellationToken`. `add_job` (line 296) submits it to a `QThreadPool` with `setMaxThreadCount(3)` (line 272). Each worker is a `_Runnable` whose `run` either calls `convert_file` directly or, when `verify_round_trip=True`, calls `_run_with_verify` (line 156).

### 3.2 Format detection

`file_detector.py:detect` (line 54):

```python
def detect(path: Path) -> str:
    ext = ext_for(path)
    try:
        with open(path, "rb") as f:
            head = f.read(4096)
    except OSError:
        return ext
    sniffed = _sniff(head, path)
    return sniffed if sniffed else ext
```

Extension first (the fast path), then magic-byte sniff on the first 4 KB. Magic-byte coverage at lines 70–119 spans PNG, JPG, GIF, BMP, WebP, TIFF, DDS, ICO, GLB, STL, PDF, RTF, JSON, HTML, SVG, and ZIP subtypes (via `_zip_subtype` — distinguishes OOXML, ODF, EPUB by ZIP central-directory contents). Compound suffixes (`.tar.gz`, `.tar.bz2`, `.tar.xz`, `.tar.zst`) are matched against full filename at lines 35–36.

### 3.3 The dispatcher

`router.py:convert_file` runs the following gates **in this order**. The first match wins.

**Gate 0 — Normalize.** `src_ext = normalize_ext(src_ext)` at line 64.

**Gate 1 — Policy enforcement** (lines 70–88). Refuses any `(src_ext, dst_ext)` where both are in `AUTO_EXECUTE_EXTS` (i.e., `.py↔.exe`) — explicitly to prevent Vitriol from being used as a malware-wrapping pipeline. Promotes `masquerade=True` automatically when the source is in `STONE_ONLY_SOURCES` (`.zip`, `.exe`).

**Gate 2 — Disk-space hint** (lines 90–101). Non-fatal status-bar warning when output target has less than `2× src_size × DISK_SPACE_WARN_MULT` free.

**Gate 3 — Trailer-envelope short-circuit** (lines 103–140). When destination is media and source is `.pdf`, `.docx`, or `.epub`, probes for a Vitriol-written trailer envelope via `_try_trailer_envelope` (line 25):

```python
def _try_trailer_envelope(src: Path, src_ext: str):
    if src_ext == ".pdf":
        from ..format_handlers.pdf_read import _try_read_trailer_envelope
        return _try_read_trailer_envelope(src)
    if src_ext in (".docx", ".epub"):
        with zipfile.ZipFile(src) as z:
            if "_vitriol/original.bin" in z.namelist():
                from ..format_handlers.masquerade import _parse_envelope
                return _parse_envelope(z.read("_vitriol/original.bin"))
    return None
```

If the envelope's recovered extension equals `dst_ext`, the payload bytes are written directly to `dst` (`router.py:113`) — byte-perfect, no re-encoding. If the recovered extension matches a different media format under the same handler, the payload is decoded, transcoded through that handler, then re-encoded.

**Gate 4 — Compiler mode** (lines 144–156). When `compiler=True` and `dst_ext == ".py"`, dispatches to `masquerade.convert` which generates a self-extracting Python script. Lossy sources are refused with a clear message.

**Gate 5 — Philosopher's Stone engage** (lines 158–183). Engages when (a) destination is a Stone host AND source is non-lossy AND source/destination are not same-handler same-category, OR (b) source carries a Stone envelope and destination is a non-Stone target. Calls `masquerade.convert(...)`.

**Gate 6 — Same-handler media** (lines 185–201). When both extensions belong to the same media handler (image/audio/video/model), the handler's own `convert()` is called. The model handler is the only one that also takes `preserve_animations`. Video→audio extractions go through `audio_video` (both registered under the same handler).

**Gate 7 — Cross-category image → document** (lines 203–222). Wraps the source's raw bytes as a single-block `TextDoc` via `image_bytes_to_textdoc` (`intermediate.py:255`), then hands off to the destination writer. `_vitriol_origin` is stashed in `doc.metadata` (line 269), so PDF/DOCX/EPUB writers that honor it can store the original bytes verbatim and PNG→PDF→PNG can be byte-perfect.

**Gate 8 — Cross-category document → media (auto-Stone)** (lines 224–241). When no semantic adapter exists and the destination is a Stone host, automatically engages Stone with a warning.

**Gate 9 — Streaming branch** (lines 259–286). When `src_size > min(streaming_threshold(src_ext), streaming_threshold(dst_ext))` (see `config.py`), and reader and writer agree they can stream the pair, dispatches to the handler's `stream_convert` method. Otherwise falls back to byte-masquerade with a status-bar warning ("byte-perfect but only round-trips through Vitriol").

**Gate 10 — Whole-file IR path** (lines 289–311):

```python
doc = reader.read(src, src_ext, cancel)
# Forward handler-stashed metadata warnings.
src_kind = getattr(reader, "DOC_KIND", "text")
dst_kind = getattr(writer, "DOC_KIND", "text")
if src_kind != dst_kind:
    adapter = ADAPTERS.get((src_kind, dst_kind))
    doc = adapter(doc)
writer.write(doc, dst, dst_ext, cancel)
```

`DOC_KIND` is one of `text` / `tabular` / `binary` / `archive` / `pandoc`. The `ADAPTERS` registry (`intermediate.py:213`) bridges kinds when needed. The pandoc bridge specifically routes ~50 markup formats through HTML so that, e.g., RST→DOCX is "Pandoc → HTML" then "Vitriol HTML → DOCX".

### 3.4 Reverse conversion and round-trip verification

`_run_with_verify` (`conversion_queue.py:156–230`) calls `convert_file` twice. First forward into `tmp_dir/forward.<dst_ext>`. Then reverse with the extensions swapped into `tmp_dir/reverse.<src_ext>`. The flags `masquerade`, `compiler`, and `password` propagate unchanged so the reverse leg engages the same Stone path. Finally:

```python
src_hash = _sha256_file(job.src)
rev_hash = _sha256_file(tmp_reverse)
if src_hash != rev_hash:
    sig.failed.emit(job.id, "Verification failed — bytes differ. ...")
    return
shutil.copy2(tmp_forward, final_dst)
```

This is the **only** in-process correctness signal Vitriol provides. The forward output is committed atomically only on hash match; otherwise both temps are wiped and the job is marked failed.

---

## 4. Lossless Conversion Analysis

| Path | Bytes preserved? | Structural / semantic preserved? | Verified by | Where loss can occur |
|---|---|---|---|---|
| **Philosopher's Stone (UCMSv1/v2/v3)** | Yes, by construction | N/A — payload is opaque bytes | Verify Round-Trip + envelope CRC | Wrong password (no oracle: produces garbage silently); corruption of the carrier file in transit |
| **Trailer envelope** (PDF/DOCX/EPUB → original media) | Yes, when envelope survives | N/A — original is opaque bytes | Verify Round-Trip | Trailer stripped by third-party rewrite (Word, Acrobat); MIME-sniffed in archive that strips unknown ZIP entries |
| **Same-handler media** (image↔image, audio↔audio, video↔video, model↔model) | No — re-encoded | Per-format only | Sample listening / visual inspection | Pillow re-encode bytes; FFmpeg fixed codec map alters bitrate/colorspace; Assimp drops bind-pose on GLB→FBX (documented in README) |
| **IR path** (DOCX, PDF, EPUB, RTF, ODT, PPTX, MD, HTML) | No | Partial — only the block types in `intermediate.py` | None | Fonts, pagination, sections, headers/footers, comments, footnotes, change tracking, OOXML w:sectPr, PDF interactive forms, EPUB CSS, doc-level metadata beyond `title/author/lang` |
| **Tabular bridge** (textdoc_to_tabular) | No | Drops prose explicitly | None | `intermediate.py:124` — prose blocks discarded; `Cell.value` cast via `str(...)` at line 116 strips numeric precision |
| **Cross-category image → document** | Yes via `_vitriol_origin` sidecar | N/A on the reversible path | Verify Round-Trip | Loss if the document is re-saved by a third party and the sidecar is dropped |
| **Image-only PDF read** | No | Not supported | — | Scanned (non-OCR) PDFs return empty TextDoc; encrypted PDFs unsupported |
| **Streaming → byte-masquerade fallback** | Yes | N/A | Verify Round-Trip | Output is byte-perfect but **only round-trips through Vitriol** — the warning at `router.py:278` says so explicitly |

**Encoding and culture.** `text_plain` decodes via `errors="replace"` at `intermediate.py:218` and `intermediate.py:222`. Non-UTF-8 source bytes are silently replaced with U+FFFD without telling the user. The `Tabular.Cell.value` field is `Union[str, int, float, bool, datetime, None]` (`intermediate.py:83`); when an XLSX cell containing `Decimal("0.1")` is round-tripped through `textdoc_to_tabular` the value becomes the string `"0.1"`, losing exact precision and type identity.

**Time and timezone.** `Cell.value` can hold `datetime`, but no timezone normalization is enforced anywhere in the pipeline; a naive `datetime` from an XLSX cell flows through as-is. The .NET 10 port should pin this to `DateTimeOffset` + invariant culture at the boundary.

**3D rigging.** The README enumerates several known caveats in detail: `glb→fbx` loses the `BindPose`/`PoseNode` chunks; `dae→fbx` round-trip on a Vitriol-generated DAE fails because Assimp can't re-read its own `$AssimpFbx$`-escaped node names; blendshapes and KHR_materials_specular degrade through Collada. These are properties of Assimp and the source-format specs, not bugs in Vitriol.

**Password-protected archive hidden in an image** (`.7z → .png → .7z`).
A common user workflow exercises Stone v3 over an already-encrypted
archive: `vitriol convert encrypted.7z hidden.png --password vitriol-pass --verify`.
The `.7z`'s own internal AES-256 is preserved as opaque payload; Vitriol's
Stone v3 AES-256-CTR (PBKDF2 from `--password`) encrypts the envelope
wrapping those bytes before the Mandelbrot LSB scatter-pack. The two
password layers are independent — recovering the `.7z` byte-for-byte
requires the Stone password; opening the contents inside requires the
7z's own password. Either password lost in isolation breaks only its
own layer, but both losses are unrecoverable. Sprint 12+ CLI behavior:
`--password` implicitly engages `--masquerade`, so a single command
suffices for either direction.

---

## 5. Bidirectional Flow

```
Forward leg (src → dst):
  src bytes ─ file_detector.detect → src_ext
            ─ router.convert_file
                 ├─ trailer-envelope short-circuit ────────────────► dst bytes (verbatim)
                 ├─ Philosopher's Stone (UCMSv*) ──┐
                 │       payload = envelope(src)   │
                 │       carrier = Mandelbrot |    │──► masquerade.convert ──► dst bytes
                 │                 music | RIFF |  │
                 │                 single-member   │
                 │                 archive | etc.  │
                 ├─ same-handler media ────────────► handler.convert ──► dst bytes
                 ├─ cross-cat img→doc ─────────────► image_bytes_to_textdoc → writer.write
                 ├─ cross-cat doc→media (Stone) ───► (same as Stone above)
                 ├─ streaming → reader.stream_convert ─► dst bytes
                 └─ IR path  reader.read ─► TextDoc/Tabular ─► ADAPTERS[(src_kind, dst_kind)]
                                          ─► writer.write ─► dst bytes

Reverse leg (dst → src), only when verify_round_trip=True:
  Exactly the same dispatcher invoked with extensions swapped. Same Stone /
  trailer / IR gates apply. The Stone path is mathematically symmetric;
  the IR path is structurally inverse but not byte-symmetric.
```

The router's **symmetry guarantee is real for Stone and trailer paths, structural only for the IR path.** When Verify Round-Trip is on, the SHA-256 compare at `conversion_queue.py:208` is the only thing that catches a non-symmetric round-trip; without that toggle, an IR-path conversion can silently lose information and the user has no signal.

---

## 6. Detailed Process Table

| Step | Existing Mechanism | Source Code Evidence | Data Preserved | Risk of Information Loss | Recommended .NET 10 C# Re-code Approach |
|---|---|---|---|---|---|
| Input file selection | UI playlist or `add_job` programmatic | `conversion_queue.py:296` | path, extension hint, password, options | None at this step | `ConversionJob` record + `IJobQueue` with `Channel<ConversionJob>` |
| Format detection | Extension first, magic-byte sniff on first 4 KB; ZIP-subtype probe for OOXML/ODF/EPUB | `file_detector.py:54–119` | Detected extension | Magic miss on truncated/malformed input | `IFormatDetector` with sniffer pipeline; reuse `System.IO.Stream` + `Span<byte>` |
| Dispatch | 9-gate decision tree in `convert_file` | `router.py:43–312` | Routing decision | Wrong gate → unsupported error or unintended Stone fallback | `IConversionRouter` with declarative gate list; each gate is `IRoutingGate.TryHandle(ConversionContext, out IConversionStep)` |
| Trailer-envelope probe | PDF trailer for PDF; `_vitriol/original.bin` ZIP entry for DOCX/EPUB | `router.py:25–40`, `pdf_read._try_read_trailer_envelope`, `masquerade._parse_envelope` | Original source bytes, original extension | Lost if third-party rewrites the container | `ITrailerEnvelopeReader` per format; share a `UcmsEnvelope` record |
| Read into IR | Per-format `reader.read(src, src_ext, cancel)` | `app/format_handlers/__init__.py` registry, e.g. `docx_handler.py:read`, `pdf_read.py:read` | Block-level structure + minimal metadata | Fonts, pagination, sections, footnotes, comments, OOXML extensions all dropped | `IFormatReader.ReadAsync(Stream, ReadContext, CancellationToken)` returning `IDocument` (TextDoc / Tabular / Binary) |
| IR data model | `TextDoc`, `Tabular`, `Block` union types | `intermediate.py:16–100` | Heading/Paragraph/List/Table/Image/CodeBlock/HR/Blockquote | Anything outside that vocabulary | Immutable C# records mirrored 1:1, plus typed `UnknownFields` bag and `OriginalBytes` sidecar |
| Cross-kind adapter | `ADAPTERS[(src_kind, dst_kind)]` lookup | `intermediate.py:213–231` | Cross-kind translation (text↔tabular↔binary↔pandoc) | `textdoc_to_tabular` drops prose explicitly | `IDocumentAdapter<TFrom, TTo>` with explicit warning when adapter is known-lossy |
| Pandoc bridge | Subprocess → HTML → Vitriol HTML reader/writer | `pandoc_handler.py`, `intermediate.py:203–210, 229–230` | HTML-expressible structure | Anything Pandoc-or-HTML can't express | `IPandocAdapter` wrapping subprocess; same HTML pivot strategy |
| Metadata propagation | `doc.metadata["warnings"]` flushed to UI status bar | `router.py:292–296` | Warnings list | — | `IProgress<ConversionEvent>` carrying `Warning` events |
| Write from IR | `writer.write(doc, dst, dst_ext, cancel)` | `pdf_write.py`, `docx_handler.py:write`, `markdown_parser.py:write` | What the writer chooses to emit | Anything not in the block model | `IFormatWriter.WriteAsync(IDocument, Stream, WriteContext, CancellationToken)` |
| Origin sidecar | `_vitriol_origin` stashed by reversibility-aware writers | `intermediate.py:269`, `pdf_write.py` (trailer), `docx_handler.py` (`_vitriol/original.bin`) | Original bytes for byte-perfect recovery | Sidecar stripped by third-party rewrite | Promote to first-class `IDocument.OriginalBytes` property; writers honor it via `[VitriolOrigin]` extension policy |
| Streaming alternative | `stream_convert` when both sides agree | `router.py:259–286`, handler `STREAMABLE` flag | Same as the non-streaming path of the same pair | Falls back to byte-masquerade when handlers don't stream | `IStreamConverter` returning `bool TryConvert(...)`; pipeline composes streaming where possible |
| Philosopher's Stone embed | UCMSv1/v2/v3 envelope + carrier (Mandelbrot, music, RIFF, etc.) | `masquerade.py:39–46, 99–337, 465–505, 935–1004`; `_stone_crypto.py:1–179`; `_mandelbrot.py`; `_music.py` | Original bytes byte-perfect | Wrong password → silent garbage (by design); carrier corruption invalidates | `IStoneEngine` with `EmbedAsync`/`ExtractAsync`; AES-CTR via `System.Security.Cryptography.AesCtr`-equivalent (CTR is not built-in; implement via `Aes.CreateEncryptor` in ECB on counter block, standard pattern); PBKDF2 via `Rfc2898DeriveBytes` |
| Stone envelope structure | `magic(8) ext_len(1) ext(var) payload_len(8 BE) payload(var) zero-pad` | `masquerade.py:465–505` | All payload bytes + extension hint | Truncation invalidates envelope | `UcmsEnvelope.WriteTo(IBufferWriter<byte>)` / `TryParse(ReadOnlySpan<byte>)` |
| Encryption | PBKDF2-HMAC-SHA256 (200k iter) → 32-byte key; deterministic IV = `HMAC(key, plaintext)[:16]`; AES-256-CTR | `_stone_crypto.py:1–179`; key cache keyed by `SHA256(password)` | Payload bytes; deterministic across runs | No auth tag → wrong-key garbage is silent (intentional no-oracle property) | Same parameters; same deterministic IV; document the no-oracle property in code comment |
| Carrier rendering | Mandelbrot fractal (image/video), procedural music (audio), single-member archive, base64 (txt), self-extract stub (py/exe) | `_mandelbrot.py`, `_music.py`, `masquerade.py` per-target functions; `tools/selfextract_stub.py` | Carrier is deterministic from envelope hash | None for carrier; payload is in LSBs | `IStoneCarrierFactory` per host; Mandelbrot uses `Vector<double>` SIMD; music synth ports the algorithm verbatim |
| Output emission | `writer.write` / `handler.convert` / `masquerade.convert` writes final dst | per-handler | Whatever the previous stage retained | — | `Stream`-based writers; atomic write via temp-and-rename |
| Reverse conversion | Recursive `convert_file` with extensions swapped | `conversion_queue.py:168–193` | Symmetric for Stone/trailer; structural for IR | IR path can lose information silently | Same `IConversionRouter` invoked with swapped extensions |
| SHA-256 verification | `_sha256_file(src)` vs `_sha256_file(tmp_reverse)` | `conversion_queue.py:197–215, 233` | Byte-equality verdict | — | `IRoundTripVerifier.VerifyAsync` returning `RoundTripResult.{ByteEqual, BytesDiffer, IoError}` |
| Commit or discard | `shutil.copy2(tmp_forward, final_dst)` on match; rmtree on fail | `conversion_queue.py:218–229` | Final output | — | `File.Move(tmp, dst, overwrite: true)` with `Path.GetTempFileName()` source |
| Cleanup | `shutil.rmtree(tmp_dir, ignore_errors=True)` | `conversion_queue.py:229` | Temp files removed | — | `try/finally` with `Directory.Delete(recursive: true)` |

---

## 7. Proposed .NET 10 C# Architecture

### 7.1 Project layout

```
Vitriol.slnx
├── Vitriol.Core/           # IRs, abstractions, router, cancellation, options
├── Vitriol.Formats.Text/   # txt, log, py, xml, html, markdown, rtf
├── Vitriol.Formats.Tabular/# csv, tsv, xlsx, parquet, feather, orc, vcf
├── Vitriol.Formats.Image/  # png, jpg, webp, bmp, tiff, gif, heic, avif, jxl, svg, ico
├── Vitriol.Formats.Media/  # ffmpeg subprocess: audio + video
├── Vitriol.Formats.Doc/    # docx, pdf, epub, odt, pptx
├── Vitriol.Formats.Archive/# zip, 7z, tar*, cbz, cb7
├── Vitriol.Formats.Model/  # 3d via AssimpNet
├── Vitriol.Formats.Font/   # otf, ttf, woff, woff2
├── Vitriol.Formats.Crypto/ # pem, crt, cer, key, der
├── Vitriol.Formats.Pandoc/ # subprocess wrapper
├── Vitriol.Stone/          # UCMSv1/v2/v3 envelope, AES-CTR, Mandelbrot, music
├── Vitriol.Bootstrap/      # FFmpeg/Pandoc/Assimp auto-fetch with SHA-256 pinning
├── Vitriol.Cli/            # System.CommandLine front-end
└── Vitriol.Tests/          # xUnit + FsCheck + golden-file suite
```

### 7.2 Core abstractions

```csharp
namespace Vitriol.Core;

public enum DocKind { Text, Tabular, Binary, Archive, Pandoc }
public enum MediaCategory { Image, Audio, Video, Model }

public interface IFormatRegistry
{
    IFormatReader?  GetReader(string ext);
    IFormatWriter?  GetWriter(string ext);
    IMediaHandler?  GetMedia(string ext);
    MediaCategory?  CategoryOf(string ext);
    bool            IsStoneOnlySource(string ext);
    bool            IsAutoExecute(string ext);
}

public interface IFormatReader
{
    DocKind Kind { get; }
    IReadOnlySet<string> SupportedExtensions { get; }
    ValueTask<IDocument> ReadAsync(Stream input, ReadContext ctx, CancellationToken ct);
}

public interface IFormatWriter
{
    DocKind Kind { get; }
    IReadOnlySet<string> SupportedExtensions { get; }
    ValueTask WriteAsync(IDocument doc, Stream output, WriteContext ctx, CancellationToken ct);
}

public interface IMediaHandler
{
    MediaCategory Category { get; }
    IReadOnlySet<string> SupportedExtensions { get; }
    ValueTask ConvertAsync(string srcExt, Stream src,
                           string dstExt, Stream dst,
                           MediaContext ctx, CancellationToken ct);
}

public interface IConversionRouter
{
    ValueTask ConvertAsync(ConversionJob job,
                           IProgress<ConversionEvent> progress,
                           CancellationToken ct);
}

public interface IRoundTripVerifier
{
    ValueTask<RoundTripResult> VerifyAsync(ConversionJob job, CancellationToken ct);
}

public interface IStoneEngine
{
    bool CanEmbedInto(string ext);
    bool CanExtractFrom(string ext);
    ValueTask<bool> HasEnvelopeAsync(Stream src, string ext, CancellationToken ct);
    ValueTask EmbedAsync(Stream src, string srcExt,
                         Stream dst, string dstExt,
                         StoneOptions opts, CancellationToken ct);
    ValueTask ExtractAsync(Stream src, string srcExt,
                           Stream dst, string dstExt,
                           StoneOptions opts, CancellationToken ct);
}
```

### 7.3 Immutable IR

```csharp
namespace Vitriol.Core.Ir;

[Flags]
public enum RunStyle { None = 0, Bold = 1, Italic = 2, Underline = 4, Code = 8 }

public abstract record Block;
public sealed record Run(string Text, RunStyle Style = RunStyle.None, string? Href = null);
public sealed record Heading(int Level, ImmutableArray<Run> Runs) : Block;
public sealed record Paragraph(ImmutableArray<Run> Runs) : Block;
public sealed record ListBlock(bool Ordered, ImmutableArray<ImmutableArray<Block>> Items) : Block;
public sealed record TableBlock(ImmutableArray<ImmutableArray<ImmutableArray<Block>>> Rows) : Block;
public sealed record ImageBlock(ReadOnlyMemory<byte> Data, string Mime,
                                string? Alt = null, int? Width = null, int? Height = null,
                                string? Href = null) : Block;
public sealed record CodeBlock(string? Language, string Text) : Block;
public sealed record HorizontalRule : Block;
public sealed record Blockquote(ImmutableArray<Block> Blocks) : Block;

public sealed record DocumentMetadata(
    string? Title, string? Author, string? Language,
    IReadOnlyDictionary<string, object> Extras,
    VitriolOrigin? Origin);

public sealed record VitriolOrigin(ReadOnlyMemory<byte> Bytes, string Extension);

public sealed record TextDoc(ImmutableArray<Block> Blocks, DocumentMetadata Metadata) : IDocument;

public sealed record Cell(object? Value, string? Formula = null);
public sealed record Sheet(string Name, ImmutableArray<ImmutableArray<Cell>> Rows);
public sealed record Tabular(ImmutableArray<Sheet> Sheets) : IDocument;

public sealed record BinaryDoc(ReadOnlyMemory<byte> Bytes, string Mime) : IDocument;
```

### 7.4 Handler stack (managed-first, subprocess where unavoidable)

| Category | Library | Notes |
|---|---|---|
| Images (PNG/JPG/WebP/BMP/TIFF/GIF/ICO/TGA/QOI) | SixLabors.ImageSharp | Pure managed, no native deps |
| Images (HEIC/HEIF/AVIF/JXL) | Magick.NET-Q8 | Native ImageMagick; auto-fetched if missing |
| Images (SVG) | Svg.Skia | Read-only initially, matches Vitriol |
| DOCX | DocumentFormat.OpenXml | Microsoft-supplied; same ZIP+XML model as Vitriol's recoded handler |
| PDF read | UglyToad.PdfPig | Structured extraction with Tj/TJ stream support |
| PDF write | QuestPDF | Document API, supports trailer extensions for `[VitriolOrigin]` |
| EPUB | VersOne.Epub + custom OPF writer | Read covered; write writes minimal OPF + nav |
| ODT/PPTX read | Open XML SDK / custom ZIP parser | Read-only, matches Vitriol scope |
| RTF read | striprtf-equivalent custom parser, or RtfPipe | Match Vitriol's read-only scope |
| XLSX | ClosedXML | MIT licensed (avoid EPPlus commercial license) |
| Parquet/Feather/ORC | Apache.Arrow + Parquet.Net | Native columnar |
| CSV/TSV | CsvHelper | Mature, handles quoting/escaping |
| Archives (zip, tar) | System.IO.Compression + SharpZipLib | Built-in zip, SharpZipLib for tar |
| Archives (7z, zstd) | SharpCompress | Pure managed |
| Fonts | SixLabors.Fonts + custom WOFF/WOFF2 round-trip | Reuse where possible |
| X.509 | System.Security.Cryptography.X509Certificates | Built-in |
| 3D | AssimpNet (P/Invoke into Assimp DLL) | Same binary Vitriol fetches; AssimpNet is the standard wrapper |
| Audio/video | FFMpegCore | Subprocess wrapper, matches Vitriol's existing `-progress pipe:1` parsing |
| Pandoc | Subprocess wrapper | No managed equivalent exists; preserve current strategy |
| Stone crypto | `System.Security.Cryptography.Aes` + `Rfc2898DeriveBytes` | AES-CTR via ECB-on-counter-block (CTR is not first-class in .NET 10); match PBKDF2 params (200k iter, SHA-256, 32-byte key) |
| Mandelbrot | `System.Numerics.Vector<double>` | SIMD port of `_mandelbrot.py` |
| Music synth | Plain managed code | Direct port of `_music.py`; 44.1 kHz stereo s16le PCM, deterministic seeded by `SHA256(payload)` |

### 7.5 Pipeline composition

`Microsoft.Extensions.DependencyInjection` registers all handlers. The router is composed of an ordered list of `IRoutingGate` instances — one per Vitriol gate — so the dispatch table is data, not control flow. `Microsoft.Extensions.Logging` replaces `app/utils/logger.py`. Configuration via `Microsoft.Extensions.Configuration` plus a `ConversionOptions` POCO. Threading uses `System.Threading.Channels.Channel<T>` for the job queue and a bounded `SemaphoreSlim` (max-concurrency 3) to mirror Vitriol's `QThreadPool` cap.

Cancellation is `System.Threading.CancellationToken` natively — Vitriol's custom `CancellationToken` is a thin shim that the .NET BCL already provides.

---

## 8. Lossless Round-Trip Strategy for .NET 10

The byte-lossless guarantee must come from a small set of mechanisms, each of which is mechanically testable.

**Canonical intermediate model.** The IR records above are the canonical model. The `OriginalBytes` field on `DocumentMetadata.Origin` is the first-class home for `_vitriol_origin`. Any writer that supports a sidecar (PDF trailer extension dictionary, DOCX `_vitriol/original.bin` ZIP entry, EPUB `META-INF/_vitriol/original.bin`) **must** honor it when present. This is enforced via an `[OriginAware]` attribute and a registry assertion at startup.

**Stable envelope.** The UCMSv1/v2/v3 magics, IV derivation, PBKDF2 parameters, AES-CTR mode, and byte layout (`magic(8) ext_len(1) ext(var) payload_len(8 BE) payload(var) zero-pad`) carry forward unchanged. The .NET port must read existing Vitriol-written files. Golden-file vectors exported from current Vitriol go in `Vitriol.Tests/Fixtures/Stone/`.

**Stable ordering.** Block ordering in `TextDoc.Blocks` is preserved by all adapters. Sheet ordering in `Tabular.Sheets` is preserved. Where ordering depends on a dictionary, use `ImmutableSortedDictionary<TKey, TValue>` or `OrderedDictionary<TKey, TValue>`.

**Stable IDs.** `_vitriol_origin` keys are namespaced under `_vitriol/`. New extensions must follow the same prefix to avoid collision with format-native metadata.

**Culture-invariant formatting.** Every `ToString`/`Parse` boundary uses `CultureInfo.InvariantCulture`. `DateTime` is converted to `DateTimeOffset` (UTC) on read; conversion back to local is the writer's choice.

**Encoding preservation.** `Vitriol.Formats.Text` detects encoding via BOM, then chardet-style sniff via `UTF.NET.AutoDetect`, and **emits a warning** when bytes that aren't valid UTF-8 are decoded with replacement — addressing the silent U+FFFD substitution Vitriol does today.

**Checksums.** `IRoundTripVerifier` computes SHA-256 with `System.Security.Cryptography.SHA256` on both endpoints. For the IR path where byte-equality is unreachable, an optional `IStructuralEquivalence` check compares the IR before write and after read, surfacing the **specific block / field** that differs.

**Golden-file tests** for every supported `(src_ext, dst_ext)` pair, drawn from `samples/` plus a synthetic corpus generated by the test project.

**Property-based tests** (FsCheck) over the IR adapters: `textdoc_to_tabular` ∘ `tabular_to_textdoc` is the identity on tabular-only docs; UCMSv1 envelope `parse ∘ build` is the identity on arbitrary payloads; AES-CTR `decrypt ∘ encrypt` is the identity for any (key, plaintext) pair.

**Schema validation** on write where a schema exists: OOXML (DOCX/XLSX), JATS, TEI, CSL-JSON, GLB, FBX. Validate failures abort the conversion rather than silently emit broken output.

**Backward-compatibility tests** read all UCMSv1/v2/v3 sample files from `samples/` and verify byte-perfect extraction.

---

## 9. Migration Plan

| Phase | Status | Objective | Main tasks | Expected output | Risks | Verification |
|---|---|---|---|---|---|---|
| 1. Codebase discovery | ✅ | Pin every assumption | This document; `samples/` round-trip baseline | This doc + a `baseline.csv` of SHA-256 for every sample's round-trip via current Vitriol | Misreading an undocumented Stone variant | Run current Vitriol on every sample with Verify Round-Trip and record hashes |
| 2. Format-contract docs | 🟡 | Spec UCMSv1/v2/v3 envelope, codec maps, magic-byte tables | Translate `masquerade.py:39–337, 465–505` and `audio_video.py` codec maps into `docs/format-contracts.md` | Format-contract spec | Spec omissions cause mismatches at phase 9 | Cross-reference with code by `grep` — envelope shape is documented inline in `Vitriol.Stone/Envelope/`; codec maps still TODO |
| 3. Intermediate model | ✅ | Port `intermediate.py` to immutable C# records | `Vitriol.Core.Ir` records + adapter registry | Solution compiles, unit tests for record equality pass | Misnaming `List` (reserved-ish) → use `ListBlock` | xUnit equality tests pass (Sprint 1) |
| 4. Adapter extraction | ✅ | Build `IFormatRegistry`, gate-based router | `IRoutingGate` impls one per current `router.py` branch | Router runs but rejects every conversion (no handlers yet) | Wrong gate order → wrong dispatch | Run with mocked handlers; assert each gate matches its Vitriol counterpart (Sprint 2) |
| 5. Reader/writer implementations | 🟡 | Implement handlers category by category | Text → Tabular → Image → Archive → Doc → Media → 3D → Stone | Each handler passes its golden-file tests | Library gaps (e.g. PdfPig vs pdfminer.six on edge fonts) | Text ✅ (Sprint 5), Image ✅ (Sprint 7), Stone hosts ✅ (Sprints 3+6+10), Tabular ✅ (Sprint 8), Archive ✅ (Sprint 9), Crypto ✅ (Sprint 11), Doc ✅ DOCX/PDF/MD (Sprint 12); EPUB / ODT / PPTX / RTF / Media / 3D / Font ⏳ |
| 6. Stone engine port | 🟡 | UCMSv1/v2/v3 byte-identical with current Vitriol | `Vitriol.Stone` project; Mandelbrot SIMD; music synth port | Stone round-trips byte-identical on every sample, including AES-encrypted | Floating-point divergence in Mandelbrot; PCM rounding in music synth | UCMSv1 + UCMSv3 envelope + AES-CTR + TXT/ZIP/WAV-v1/AIFF-v1 ✅ (Sprints 3+6); PNG v1+v3 ✅ (Sprints 3+10, v3 fractal pixel-byte parity with NumPy not guaranteed); audio-v3 music synth, MKV-v3, 3D-v3 ⏳ |
| 7. Round-trip verifier | ✅ | `IRoundTripVerifier` + structural-equivalence for IR | Implement plus xUnit harness | Verifier runs on every sample, reports byte-equal or specific structural diff | Structural comparator misses semantic equality (e.g. trailing whitespace) | All 5 v1 Stone hosts report `ByteEqual` end-to-end via DI (Sprint 4) |
| 8. Bootstrap | ⏳ | Port `launcher.py` for FFmpeg/Pandoc/Assimp auto-fetch | `Vitriol.Bootstrap` with HTTPS download + SHA-256 verification + arch detection | Fresh machine bootstraps to working CLI in one command | Upstream URL drift; signature changes | Pin SHA-256 in code, CI fetches and verifies weekly |
| 9. Parallel run | ⏳ | Run current Vitriol and Vitriol.Net on the same corpus | CI harness that converts every supported `(src_ext, dst_ext)` pair through both implementations | Hash-equal report for Stone/trailer paths; structural-equal report for IR path | Per-format library quirks surface late | Compare SHA-256 (Stone) and IR-structural-equality (IR) for every pair |
| 10. CLI release | 🟡 | Package `Vitriol.Cli` as a self-contained single-file native AOT (if feasible) executable | NativeAOT trimming, dependency pinning, release notes | `vitriol convert input.png output.pdf` works on Win/macOS/Linux | AOT-incompatible libraries (ImageSharp historically had issues) | CLI binary ✅ (Sprint 5; smoke tests cover stream pass-through + Stone+verify + image transcode); NativeAOT publish ⏳ |

GUI is explicitly out of scope. A follow-on phase 11 can re-introduce Avalonia or MAUI once the engine is solid.

---

## 10. Testing Plan

The current repo ships **zero automated tests**. The .NET port must reverse that.

- **Unit tests** (xUnit): IR equality and adapters; envelope parse/build; magic-byte detection; PBKDF2 known-answer test vectors (`Rfc2898DeriveBytes` against published RFC 8018 vectors); AES-CTR known-answer vectors (NIST SP 800-38A); SHA-256 file hash against an external reference.
- **Integration tests**: every `(src_ext, dst_ext)` pair from `Vitriol.Formats.*` registry; reader-then-writer-then-reader equality on golden inputs; round-trip via `IRoundTripVerifier` on all `samples/` fixtures.
- **Round-trip conversion tests**: forward, reverse, SHA-256 compare for Stone/trailer paths; structural-equivalence compare for IR paths.
- **Golden-file tests**: every sample under `samples/Sample Music Outputs/`, `samples/The Raven Outputs/`, `samples/Chopin - Nocturne Outputs/`, plus a synthetic corpus generated at test time. Each golden file is the byte-exact output of current Vitriol — if the .NET port produces the same bytes, behavior is preserved.
- **Property-based tests** (FsCheck): envelope `parse ∘ build = id`; AES-CTR `decrypt ∘ encrypt = id`; UTF-8 round-trip; XLSX cell-value round-trip without `str()` cast.
- **Fuzz tests**: malformed envelopes (truncated, magic-overwritten, payload_len > host_size); corrupt PNGs (CRC mismatch, zlib error); empty/oversized inputs; non-UTF-8 byte sequences claiming UTF-8 BOM.
- **Large-file tests**: ≥ 2 GB Stone embed/extract; ≥ 2 GB FFmpeg pass-through; verify the streaming branch is taken (`router.py:259–286` analog).
- **Encoding tests**: UTF-8 with/without BOM, UTF-16 LE/BE, CP1252, GB18030. Specifically test the failure path that `intermediate.py:218,222` papers over silently.
- **Performance benchmarks** (BenchmarkDotNet): Mandelbrot rendering (target: ≥ NumPy parity via `Vector<double>`); music synth; envelope encrypt/decrypt; PNG/WAV/MKV embed throughput.
- **Stone determinism**: given identical `(payload, password)`, the output must be SHA-256 identical across runs and across platforms. This is the property that makes "Verify Round-Trip" trustworthy.

Specific tests derived from current Vitriol behavior:

1. **PNG↔PDF byte-perfect via `_vitriol_origin`** — must survive PDF reader → PDF writer → PDF reader.
2. **GLB→FBX bind-pose drop** — assert the bind-pose is missing (matches README scope note); document as known caveat.
3. **DAE→FBX of Vitriol-generated DAE fails** — assert the failure mode matches Vitriol's; surface the documented workaround.
4. **`textdoc_to_tabular` drops prose** — assert the warning is emitted, not silent.
5. **Wrong-password Stone extract yields garbage, not error** — preserves no-oracle property.

---

## 11. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Hidden information loss in IR path (DOCX/PDF/EPUB) | Structural-equivalence verifier + `OriginalBytes` sidecar enforced via `[OriginAware]` attribute. Surface, do not hide, any block-level drop. |
| Format ambiguity in fixed FFmpeg codec maps | Surface codec/profile options as `MediaContext` properties; default to Vitriol's current map for parity. |
| Mandelbrot SIMD precision differences vs. NumPy | Use `Vector<double>` with the exact same iteration scheme; gate on a byte-equal golden-image test in CI. Fall back to scalar path when `Vector.IsHardwareAccelerated` is false. |
| FFmpeg / Assimp / Pandoc binary version drift | SHA-256-pinned auto-fetch in `Vitriol.Bootstrap`; weekly CI run verifies upstream URLs haven't moved; refuse to start if a downloaded binary's SHA-256 doesn't match the pinned value. |
| .NET `DateTime` vs. Python `datetime` | Standardize on `DateTimeOffset` UTC at the boundary; document timezone policy in `IDocument.Metadata.Extras`. |
| Floating-point precision through Tabular text bridge | Preserve `Cell.Value` as `object?` and serialize via type-aware writer instead of `str()`; never lose `decimal` precision in XLSX. |
| EPPlus commercial license | Use ClosedXML instead (MIT). |
| Elastic License (Vitriol's source-available license) | Port preserves attribution; redistribution of the port is itself bound by the Elastic License terms. Re-read `LICENSE` before any external release. |
| AES-CTR not first-class in .NET 10 | Implement CTR via `Aes.CreateEncryptor` in ECB mode on the counter block. Match Python `_stone_crypto.py` byte-for-byte; gate on known-answer vectors. |
| Subprocess argument injection (FFmpeg, Pandoc, Assimp) | Use `ProcessStartInfo.ArgumentList` (not `Arguments`); never shell-out via `/bin/sh -c`. |
| Path-traversal in archive handlers | Validate every archive entry path against the destination root before extraction; reject `..` and absolute paths. (Vitriol's current archive handler should be re-audited; the .NET port enforces this in `ArchiveExtractor.ValidateEntryPath`.) |
| Self-extracting `.py`/`.exe` Stone outputs being flagged as malware | Document the threat-model trade-off; sign EXE outputs when a code-signing cert is configured (mirroring `tools/sign.py`); never include eval/exec on untrusted input. |
| Performance regression on Stone video carrier (MKV) | Use FFmpeg pipe-frames-in via `FFMpegCore` async streaming; benchmark against Python+NumPy baseline. |

---

## 12. Final Recommendations

**The current mechanism is suitable as a structural model for the .NET 10 port.** The router's gate design, the two-IR split, the handler registry, the envelope format, and the SHA-256 Verify Round-Trip are all worth preserving. Specifically:

**Reuse conceptually:**

- The 9-gate router. Translate to data-driven `IRoutingGate` list.
- `TextDoc` / `Tabular` IRs. Port to immutable C# records.
- UCMSv1/v2/v3 envelope layout, magics, PBKDF2 parameters, deterministic IV. **Do not change these** — backward compatibility with existing Stone outputs is non-negotiable.
- The `_vitriol_origin` sidecar pattern. Promote to first-class `DocumentMetadata.Origin`.
- The Verify Round-Trip flow. SHA-256 compare is exactly right.
- The launcher's auto-fetch model. Port directly to `Vitriol.Bootstrap`.

**Redesign:**

- The IR vocabulary should grow a typed `UnknownFields` bag so handler-specific metadata (Vitriol's `metadata: dict` pattern) is typed at the surface, not stringly-typed.
- `Cell.value` should remain `object?` and serializers should respect the original type rather than going through `str()`.
- Encoding decoding should warn loudly, not silently replace.
- Add structural-equivalence verification for the IR path so users have a signal when the round-trip is structurally lossy.
- Tests are mandatory. The current zero-test posture is the single biggest risk in the codebase; the port reverses it.

**Must test before claiming losslessness:**

- Every UCMSv1/v2/v3 sample from `samples/` extracts byte-identically.
- Stone determinism: same `(payload, password)` always produces the same carrier SHA-256.
- Verify Round-Trip catches every IR-path information loss the port introduces — i.e., when the port re-runs `_run_with_verify` against the current Vitriol's known-failing pairs (e.g., DOCX with comments → MD → DOCX), the port should also fail rather than silently succeed.
- The 3D caveats documented in `README.md` are reproduced exactly (`glb→fbx` bind-pose drop is the property under test, not a bug).

**Honesty in user-facing copy.** Vitriol's README markets the Philosopher's Stone path as byte-lossless — correctly. The .NET CLI must not market the IR path the same way. The `vitriol convert` command should print a one-line warning when it dispatches through the IR path: *"This conversion uses Vitriol's intermediate document model; pagination, fonts, comments, and section breaks may be lost. Run with `--verify` to detect byte-level drift on round-trip."*

That single sentence, in production, is the most important deliverable of this entire migration.

---

## Appendix: Evidence Index

The following source files were inspected for this document. Citations in the body above use `path:line` form; this appendix lists the files themselves.

- `app/core/router.py` — 312 LOC; `convert_file` dispatcher
- `app/core/intermediate.py` — 270 LOC; IRs and adapters
- `app/core/conversion_queue.py` — 344 LOC; threading, Verify Round-Trip
- `app/core/file_detector.py` — 151 LOC; extension + magic-byte detection
- `app/core/config.py` — 111 LOC; streaming threshold, disk-space helpers
- `app/format_handlers/__init__.py` — handler registry, policy sets
- `app/format_handlers/masquerade.py` — 6065 LOC; Stone engine
- `app/format_handlers/_stone_crypto.py` — 179 LOC; PBKDF2 + AES-256-CTR
- `app/format_handlers/_mandelbrot.py` — fractal carrier
- `app/format_handlers/_music.py` — music carrier synthesis
- `app/format_handlers/image_handler.py` — Pillow-based image handler
- `app/format_handlers/audio_video.py` — FFmpeg subprocess wrapper
- `app/format_handlers/docx_handler.py` — DOCX read/write
- `app/format_handlers/pdf_read.py` — pdfminer.six + fallback FlateDecode
- `app/format_handlers/pdf_write.py` — reportlab writer, trailer-envelope-aware
- `app/format_handlers/archive_handler.py` — zip/7z/tar family
- `app/format_handlers/model_handler.py` — Assimp wrapper
- `app/format_handlers/pandoc_handler.py` — Pandoc subprocess wrapper
- `app/format_handlers/markdown_parser.py` — bundle-aware MD round-trip
- `launcher.py` — dependency bootstrap
- `main.py` — PySide6 GUI startup (out of scope for CLI port)
- `README.md` — format inventory and 3D animation caveats
- `samples/` — manual round-trip fixtures

**Confirmed absent:** `tests/` directory, `test_*.py`, `pytest.ini`, `tox.ini`. The .NET port introduces these for the first time.
