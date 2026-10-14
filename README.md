# Line by Line

A lightweight Windows PDF viewer for **line-by-line data entry**. It spotlights one or more lines of an invoice (or any document), dims the rest of the page, and lets you step to the next line with a global hotkey, without leaving Excel.

> Repo: https://github.com/silent-diffusion/Line-By-Line-PDF-Viewer
> Status: **v0.2**: everything in v0.1 plus resizable band and column focus

---

## Why it exists

Typing invoice line items into a spreadsheet means constantly switching between the PDF and Excel and finding your place again each time. Line by Line keeps your place for you:

1. Open an invoice. A **focus band** sits across the first line item.
2. Read it, switch to Excel, and type it in.
3. Press **Ctrl+Alt+Down** from inside Excel. The band moves to the next line.

You never click back into the viewer.

## Use cases

- **Accounts payable data entry**: the primary use. Vendor invoices go line by line into a spreadsheet or ERP.
- **Reconciliation**: compare bank or credit card statements against a ledger, one transaction at a time.
- **Auditing and checking**: compare a PDF against an existing spreadsheet row by row.
- **Packing slips and POs**: check received quantities against what was ordered.
- **Price lists and catalogs**: move vendor pricing into a system.
- **Reading focus**: works as a general reading ruler for dense documents.

---

## Features

### Focus band (spotlight)
- One or more lines are highlighted. The band appears **lifted** (soft shadow, slight brightness) and the rest of the page is **dimmed but still readable**.
- **Lines in view**: how many lines the band covers (default: 2).
- **Step size**: how far one keypress moves the band (default: same as lines in view).
- **Dim strength**: adjustable.
- **Snaps to real text lines**: the band follows the document's actual rows, not a fixed pixel height, so uneven row spacing is handled. A fixed-height mode is the fallback for messy layouts.
- **Grab, drag and resize**: the band can be dragged anywhere and resized on all edges, including **width** (to focus one column or table). It snaps to the nearest line.
- **Smart next/previous**: after you resize or reposition the band, the app works out what you're focused on (e.g. the line-items table, or a certain column span) and moves *within that region*. It skips to the next matching row instead of blindly moving down by a fixed amount.
- **Remembers your place** in each file, so reopening an invoice resumes on the same line.

### Line detection
- **Digital PDFs**: text positions are read straight from the PDF, so detection is exact and instant.
- **Scanned PDFs and images** (PNG, JPG, TIFF): OCR finds where the text is on the page using the **built-in Windows OCR engine** (offline, nothing extra to install).

### Global hotkeys (work while Excel has focus)
All rebindable in Settings.

| Action | Default |
|---|---|
| Next line(s) | `Ctrl+Alt+Down` |
| Previous line(s) | `Ctrl+Alt+Up` |
| Next / previous page | `Ctrl+Alt+PgDn` / `Ctrl+Alt+PgUp` |
| Toggle dimming | `Ctrl+Alt+D` |
| Copy value (when Value Grab is on) | `Ctrl+Alt+C` |
| Toggle always-on-top | `Ctrl+Alt+T` |

> Note: some Intel graphics drivers use `Ctrl+Alt+Arrow` to rotate the screen. If that happens, turn off the driver hotkeys or rebind them in Line by Line.

### Value Grab (optional, off by default)
- Detects **dollar amounts** inside the focus band and shows them as small chips next to it.
- The hotkey copies the amount to the clipboard so you can paste it straight into Excel.
- **Default output: dollar amount only.** Optional mode: copy the whole row as tab-separated values, which fill across Excel columns when pasted.
- Can be **fully disabled** in Settings, which removes the chips and the hotkey.
- On scanned documents OCR can misread digits, so always check visually.

### Window
- **Always on top**: toggle in Settings (default: off).
- Open files by dragging them onto the window or with File → Open.
- Built for a two-monitor setup (PDF on one screen, Excel on the other) but works on one.

---

## Install & updates

### Install
- Download **`LineByLine-Setup.exe`** from the [Releases](https://github.com/silent-diffusion/Line-By-Line-PDF-Viewer/releases) page and run it. Install takes one click, needs no admin rights, and adds a Start menu shortcut.
- The installer is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**.

### Updates (Settings → Updates)
- **Check for Updates** button: checks the latest GitHub release.
  - If one exists, the app shows the **new version number and its release notes** (a short description of what changed) and asks **Update & restart now** or **Later**.
- **Auto-update** toggle: when on, the app checks every time it opens and installs updates automatically.
- Updates are delta packages, so they're small and fast.

---

## Using v0.1

- **Open** a PDF with the file button, `Ctrl+O`, or drag it onto the window.
- **Move the band** with `Ctrl+Alt+↓` / `Ctrl+Alt+↑` from any app, or with `↓`/`↑` (also `J`/`K`) while Line by Line has focus.
- **Place the band** by clicking any line, or by dragging the band itself. It snaps to the nearest line.
- **Resize the band** by dragging its edges (handles appear on hover). Top and bottom change how many lines it covers.
- **Column focus**: drag the band's left or right edge to narrow it to some columns, e.g. Qty → Amount. The edge snaps to the nearest column. From then on the band stops only on lines with the same layout as the one it's on, so it skips wrapped descriptions, table headers and notes, and carries on into the same table on the next page. A dashed outline shows those lines. Double-click the band, press `Esc`, or click **Column focus ✕** in the bottom bar to go back to full-width lines.
- **Band size and step**: the `Band` controls in the top bar, or Settings → Focus band.
- **Zoom**: `Ctrl+scroll`, `Ctrl+=`/`Ctrl+-`, and `Ctrl+0` to fit the page width.
- **Layout**: when the window is taller than it is wide (a portrait monitor), the thumbnails shrink to a slim page rail and the page fills the width.

Settings are saved in `%AppData%\LineByLine\settings.json`.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows 10/11.

```bash
dotnet run --project src/LineByLine
```

```bash
dotnet test
```

`samples/make_sample_invoice.py` regenerates `samples/sample-invoice.pdf`, the test invoice (fictional data). To save screenshots of the real window in landscape, portrait and Settings, set `LBL_SCREENSHOT_DIR` to a folder and run `dotnet test --filter Category=Visual`.

## Tech stack

| Part | Choice |
|---|---|
| App | C# / WPF on .NET 10 |
| PDF rendering + text positions | PDFium |
| OCR | Windows.Media.Ocr (built into Windows 10/11) |
| Global hotkeys | Win32 `RegisterHotKey` |
| Installer + updater | Velopack, publishing to GitHub Releases |
| CI / releases | GitHub Actions: tag `vX.Y.Z` → build → publish installer + update packages |

Target: Windows 10/11, x64.

## Roadmap

- **v0.1** ✅: open PDF, focus band (lines in view, step size, dim), global hotkeys, settings, click/drag to place the band, resume position, landscape/portrait layouts
- **v0.2** ✅: resize the band on all edges; column focus (narrow the band and it steps only through lines with the same column layout, across pages); remembered per file
- **v0.3**: OCR for scanned PDFs and images
- **v0.4**: Value Grab (dollar amount default, row mode, full disable)
- **v0.5**: installer, Settings → Updates, auto-update via GitHub Releases
- **Later**: batch / multi-file queue, code signing

## License
TBD
