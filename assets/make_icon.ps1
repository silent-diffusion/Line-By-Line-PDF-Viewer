# Draws the Line by Line icon (two grey lines with a blue focus band between them, on a
# rounded white tile) at the standard icon sizes and packs them into assets/app.ico.
# Run from the repo root in Windows PowerShell:  powershell -File assets/make_icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = 2 * $r
        $p.AddArc($x, $y, $d, $d, 180, 90)
        $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
        $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure()
        return $p
    }

    $u = $s / 32.0  # design grid is 32 x 32
    # Tile
    $tile = RoundRect (1 * $u) (1 * $u) (30 * $u) (30 * $u) (7 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 249, 249, 250))), $tile)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 214, 215, 219)), ([Math]::Max(1, 1 * $u))), $tile)

    # Grey text lines above and below
    $grey = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 160, 163, 170)), (2.6 * $u)
    $grey.StartCap = $grey.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($grey, 7 * $u, 8.5 * $u, 25 * $u, 8.5 * $u)
    $g.DrawLine($grey, 7 * $u, 23.5 * $u, 25 * $u, 23.5 * $u)

    # Blue focus band
    $band = RoundRect (5 * $u) (12.5 * $u) (22 * $u) (7 * $u) (2.4 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 233, 241, 255))), $band)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 91, 155, 255)), (2.4 * $u)), $band)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $g.Dispose(); $bmp.Dispose()
}

# ICO container with PNG-compressed entries (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
$target = Join-Path $PSScriptRoot "app.ico"
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot "app-256.png"), $pngs[-1])
Write-Output "wrote $target"
