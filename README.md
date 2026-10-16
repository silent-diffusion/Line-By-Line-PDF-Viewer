# Line by Line

A lightweight Windows PDF viewer for **line-by-line data entry**. It spotlights one or more lines of a document, dims the rest of the page, and lets you step to the next line with a global hotkey, without leaving Excel.

> Repo: https://github.com/silent-diffusion/Line-By-Line-PDF-Viewer
> Status: **v0.8**: dark theme and number-pad shortcuts. Installer and in-app updates since v0.5. [Download the latest release](https://github.com/silent-diffusion/Line-By-Line-PDF-Viewer/releases/latest)

---

## Why it exists

Typing a document's lines into a spreadsheet means constantly switching between the PDF and Excel and finding your place again each time. Line by Line keeps your place for you:

1. Open a document. A **focus band** sits across the first line.
2. Read it, switch to Excel, and type it in.
3. Press **Ctrl+Alt+Down** from inside Excel. The band moves to the next line.

You never click back into the viewer.

## Use cases

- **Data entry**: the primary use. Bills, orders and statements go line by line into a spreadsheet or ERP.
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
- **Remembers your place** in each file, so reopening a document resumes on the same line.

### Line detection
- **Digital PDFs**: text positions are read straight from the PDF, so detection is exact and instant.
- **Scanned PDFs and images** (PNG, JPG, TIFF, BMP): OCR finds where the text is on the page using the **built-in Windows OCR engine** (offline, nothing extra to install). Each page is read at a few resolutions and the words are merged, because Windows OCR tends to drop short, isolated numbers (a quantity alone in its column) and which ones it drops changes with resolution. The band works just like on a digital PDF, column focus included. The bottom bar shows **OCR** on those pages, as a reminder to check digits.

### Global hotkeys (work while Excel has focus)
All rebindable in Settings → Hotkeys. Each action can also have a **second shortcut**; by default the number pad gets `Ctrl+Alt+Num 2` / `Ctrl+Alt+Num 8` (next/previous) and `Ctrl+Alt+Num +` (highlight).

| Action | Default |
|---|---|
| Next line(s) | `Ctrl+Alt+Down` |
| Previous line(s) | `Ctrl+Alt+Up` |
| Next / previous page | `Ctrl+Alt+PgDn` / `Ctrl+Alt+PgUp` |
| Toggle dimming | `Ctrl+Alt+D` |
| Copy value of the top line (Value Grab on) | `Ctrl+Alt+C` |
| Copy values of every line in the band (Value Grab on) | `Ctrl+Alt+Shift+C` |
| Toggle always-on-top | `Ctrl+Alt+T` |
| Highlight line (press again to remove) | `Ctrl+Alt+.` |

> Note: some Intel graphics drivers use `Ctrl+Alt+Arrow` to rotate the screen. If that happens, turn off the driver hotkeys or rebind them in Line by Line.
>
> `Ctrl+Alt+` the number-pad period can't be used: it's the same key as Delete, and Windows keeps Ctrl+Alt+Delete for itself before any app sees it. Use `Ctrl+Alt+.` on the main keyboard, or `Ctrl+Alt+Num +`.

### Value Grab (optional, off by default)
- Turn it on with the **$ Value Grab** button in the toolbar or in Settings → Value Grab.
- Finds each highlighted line's **value**. By default that's the **rightmost value on the line**, which is usually the line total. Percentages, dates and codes like `PA-2011` are skipped. Alternatively, **rightmost dollar amount** only takes numbers written as money (`12.50`, `$1,825`), skipping quantities. With column focus on, only the focused columns are used, e.g. focus on the Unit column to grab unit prices.
- Repairs common OCR slips before reading a number: `425. 00`, `1, 825.59`, `$ 9.99`, `1O5.5O`, `l2.00`, `425.00.`.
- **Auto grab** (Settings → Value Grab): copies automatically every time the band moves, either the top line or every line in the band. If a line has no value, a message says so and the clipboard is left alone.
- Shows the amounts as chips beside the band, or in the bottom bar when there's no room beside it (portrait, wide pages). Click a chip to copy it.
- `Ctrl+Alt+C` copies the top line's amount; `Ctrl+Alt+Shift+C` copies every line in the band, one per row, so they paste down an Excel column.
- Amounts are copied as plain numbers Excel reads correctly: `$1,825.59` → `1825.59`, `(45.00)` and `45.00 CR` → `-45.00`.
- **Default: dollar amount only.** Optional mode copies the whole line as tab-separated cells, which fill across Excel columns when pasted.
- **Fully off when disabled:** no chips, and the copy hotkeys aren't even registered, so other apps can use them. The toolbar button can be hidden too.
- On pages read by OCR, the "Copied" message reminds you to check the digits.

### Highlights
- Press `Ctrl+Alt+.` to highlight the line the band is on, like a highlighter pen; press it again to remove it. Highlights stay as the band moves on, so you can see which lines are done, and they're remembered per file.
- **Settings → Highlights:**
  - Colour: green by default, presets or a custom `#RRGGBB`, and strength.
  - What: the whole line (default; only the focused columns under column focus) or **just the value**, the same number Value Grab copies.
  - Which lines: the top line of the band (default) or every line in it.
  - **Highlight lines automatically** as the band moves forward past them (off by default).
  - **Save highlights into the PDF file** (off by default): writes standard PDF highlight annotations so Acrobat, Edge, Chrome and other viewers show them. Off means the file is never modified. Images and scans keep their highlights in the app.
  - **Clear all highlights** for the open document, including any saved inside the PDF.

### Appearance
- **Light or dark theme.** By default it follows Windows' app mode (Settings → Personalization → Colors) and switches live when that changes; choose Light or Dark in Settings → Appearance to override. Pages stay white in both.

### Window
- **Always on top**: toggle in Settings (default: off).
- Open files by dragging them onto the window or with File → Open.
- Built for a two-monitor setup (PDF on one screen, Excel on the other) but works on one.

---

## Install & updates

### Install
- Download **`LineByLine-win-Setup.exe`** from the [latest release](https://github.com/silent-diffusion/Line-By-Line-PDF-Viewer/releases/latest) and run it. Install takes one click, needs no admin rights, and adds a Start menu shortcut. It installs to `%LocalAppData%\LineByLine`; uninstall from Windows Settings → Apps.
- The installer is about 80 MB because it includes .NET, so nothing else needs installing. Updates after that download only what changed.
- The installer is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**.

### Updates (Settings → Updates)
- **Check for Updates** button: checks the latest GitHub release.
  - If one exists, the app shows the **new version number and its release notes** (a short description of what changed) and asks **Update & restart now** or **Later**.
- **Automatic updates** switch (off by default): when on, the app checks GitHub every time it opens and downloads any new version in the background. If no document is open yet, it installs and restarts right away; otherwise it installs quietly when you close the app, so it never interrupts your work.
- Updates are delta packages, so they're small and fast.

---

## Using v0.1

- **Open** a PDF or a scanned image with the file button, `Ctrl+O`, or drag it onto the window. Scans are read with OCR in the background (about a second per page); the band snaps to real lines as soon as each page is done.
- **Move the band** with `Ctrl+Alt+↓` / `Ctrl+Alt+↑` from any app, or with `↓`/`↑` (also `J`/`K`) while Line by Line has focus.
- **Place the band** by clicking any line, or by dragging the band itself. It snaps to the nearest line. A click that only brings the window to the front is ignored.
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

`samples/make_sample_invoice.py` regenerates `samples/sample-invoice.pdf`, the test document (fictional data). To save screenshots of the real window in landscape, portrait and Settings, set `LBL_SCREENSHOT_DIR` to a folder and run `dotnet test --filter Category=Visual`.

## Releasing a new version

1. Bump `<Version>` in `src/LineByLine/LineByLine.csproj`.
2. Add `release-notes/<version>.md`. Its text is what users see under "What's new" in Settings → Updates.
3. Commit, then tag and push:

```bash
git tag v0.5.1
```

```bash
git push origin main v0.5.1
```

The [Release workflow](.github/workflows/release.yml) runs the tests, builds a self-contained app, packs it with Velopack (installer, full and delta update packages) and publishes the GitHub release. Installed copies pick it up from there.

## Tech stack

| Part | Choice |
|---|---|
| App | C# / WPF on .NET 10 |
| PDF rendering + text positions | PDFium |
| OCR | Windows.Media.Ocr (built into Windows 10/11) |
| Global hotkeys | Win32 `RegisterHotKey` |
| Installer + updater | Velopack, publishing to GitHub Releases |
| Writing highlights into PDFs | PDFsharp |
| CI / releases | GitHub Actions: tag `vX.Y.Z` → build → publish installer + update packages |

Target: Windows 10/11, x64.

## Roadmap

- **v0.1** ✅: open PDF, focus band (lines in view, step size, dim), global hotkeys, settings, click/drag to place the band, resume position, landscape/portrait layouts
- **v0.2** ✅: resize the band on all edges; column focus (narrow the band and it steps only through lines with the same column layout, across pages); remembered per file
- **v0.3** ✅: OCR for scanned PDFs and images (PNG, JPG, TIFF incl. multi-page, BMP), with a Settings switch
- **v0.4** ✅: Value Grab (dollar amount default, whole-row mode, copy one or all lines, full disable, toolbar button can be hidden)
- **v0.5** ✅: one-click installer, Settings → Updates (check, release notes, update & restart), automatic updates, app icon
- **v0.6** ✅: auto grab; rightmost-value pick (default) or rightmost dollar amount; OCR number repair
- **v0.7** ✅: highlights (hotkey, colour, whole line or value, top or all lines, auto-highlight, optional saving into the PDF)
- **v0.8** ✅: dark theme following Windows (with Light/Dark override); second shortcuts with number-pad defaults
- **Later**: batch / multi-file queue, code signing

## License
TBD
