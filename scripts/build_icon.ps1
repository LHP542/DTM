# DTM App-Icon-Generator (PowerShell-Port von build_icon.py).
#
# Warum zwei Varianten: der Arbeitslaptop hat kein Python/Pillow, Bazzite
# schon. Gleiche Geometrie, gleiche Farben, gleiches Ergebnis - bei
# Design-Aenderungen BEIDE Skripte anpassen.
#
# Erzeugt:
#   DTM/Assets/dtm.png   (256x256, master fuer Fenster/Tray/AppImage)
#   DTM/Assets/dtm.ico   (multi-res 16..256 fuer <ApplicationIcon>)
#
# Aufruf:  pwsh -File scripts/build_icon.ps1
#
# Reines ASCII im Quelltext (Windows-PowerShell-5.1-ANSI-Falle).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

# Kroste-Palette (siehe DTM/App.axaml)
$BG        = [System.Drawing.Color]::FromArgb(255, 18, 62, 107)    # #123E6B
$FG        = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)  # #FFFFFF
$FG_SHADOW = [System.Drawing.Color]::FromArgb(255, 220, 228, 240)
$ACCENT    = [System.Drawing.Color]::FromArgb(255, 224, 177, 76)   # #E0B14C
$ACCENT_R  = [System.Drawing.Color]::FromArgb(255, 168, 128, 45)   # #A8802D

$CORNER = 48

function New-Graphics([System.Drawing.Bitmap]$bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    return $g
}

function Add-RoundedRect($g, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r, $brush) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function Add-Background($g, [int]$size, [double]$scale) {
    # Bei sehr kleinen Groessen kleinere Rundung, sonst wirkt es rund.
    $corner = if ($size -ge 48) { [int]($CORNER * $scale) } else { [Math]::Max(2, [int]($size * 0.14)) }
    $b = New-Object System.Drawing.SolidBrush $BG
    Add-RoundedRect $g 0 0 ($size - 1) ($size - 1) $corner $b
    $b.Dispose()
}

function New-IconLarge([int]$size) {
    # Volles Design fuer >= 64px: Zylinder mit 3 Scheiben (2 Rillen),
    # Akzent-Punkt oben rechts.
    $scale = $size / 256.0
    $bmp = New-Object System.Drawing.Bitmap $size, $size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    Add-Background $g $size $scale

    $bFg     = New-Object System.Drawing.SolidBrush $FG
    $bShadow = New-Object System.Drawing.SolidBrush $FG_SHADOW
    $bAccent = New-Object System.Drawing.SolidBrush $ACCENT

    $cx     = $size / 2.0
    $rX     = [int](60 * $scale)
    $rY     = [int](15 * $scale)
    $top    = [int](82 * $scale)
    $gap    = [int](52 * $scale)
    $stroke = [Math]::Max(2, [int](3 * $scale))
    $pBg    = New-Object System.Drawing.Pen $BG, $stroke

    $bodyBot = $top + 2 * $gap
    $g.FillRectangle($bFg, ($cx - $rX), $top, (2 * $rX), ($bodyBot - $top))
    $g.FillEllipse($bShadow, ($cx - $rX), ($bodyBot - $rY), (2 * $rX), (2 * $rY))

    for ($i = 0; $i -lt 3; $i++) {
        $y = $top + $i * $gap
        if ($i -eq 0) {
            # Oberste Scheibe voll (der eigentliche Deckel)
            $g.FillEllipse($bFg, ($cx - $rX), ($y - $rY), (2 * $rX), (2 * $rY))
            $g.DrawEllipse($pBg, ($cx - $rX), ($y - $rY), (2 * $rX), (2 * $rY))
        } else {
            # Rille: nur den UNTEREN Halbbogen zeichnen (0..180 Grad). Der
            # frueher genutzte Weg "volle Ellipse zeichnen, obere Haelfte mit
            # einem Rechteck uebermalen" liess an den Zylinderkanten Reste der
            # Umrisslinie stehen und sah bei 256px ausgefranst aus.
            $g.DrawArc($pBg, ($cx - $rX), ($y - $rY), (2 * $rX), (2 * $rY), 0, 180)
        }
    }

    # Akzent-Punkt (Gold) oben rechts
    $dotR  = [int](20 * $scale)
    $dotCx = [int](206 * $scale)
    $dotCy = [int](50 * $scale)
    $pAcc  = New-Object System.Drawing.Pen $ACCENT_R, ([Math]::Max(1, [int](1.5 * $scale)))
    $g.FillEllipse($bAccent, ($dotCx - $dotR), ($dotCy - $dotR), (2 * $dotR), (2 * $dotR))
    $g.DrawEllipse($pAcc, ($dotCx - $dotR), ($dotCy - $dotR), (2 * $dotR), (2 * $dotR))

    $pAcc.Dispose(); $pBg.Dispose()
    $bFg.Dispose(); $bShadow.Dispose(); $bAccent.Dispose()
    $g.Dispose()
    return $bmp
}

function New-IconSmall([int]$size) {
    # Vereinfachte Variante fuer 16..48px (Windows-Taskbar, Explorer).
    # Aggressives Padding, damit der blaue Grund nicht verschwindet;
    # nur Deckel + Boden, keine Rillen, kein Akzent-Punkt.
    $bmp = New-Object System.Drawing.Bitmap $size, $size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    Add-Background $g $size 1.0

    $bFg     = New-Object System.Drawing.SolidBrush $FG
    $bShadow = New-Object System.Drawing.SolidBrush $FG_SHADOW

    $cx   = $size / 2.0
    $padX = [Math]::Max(2, [int]($size * 0.28))
    $rX   = $size / 2.0 - $padX
    $topY = [int]($size * 0.28)
    $botY = [int]($size * 0.78)
    $rY   = [Math]::Max(2, [int]($size * 0.09))

    $g.FillRectangle($bFg, ($cx - $rX), $topY, (2 * $rX), ($botY - $topY))
    $g.FillEllipse($bShadow, ($cx - $rX), ($botY - $rY), (2 * $rX), (2 * $rY))
    $g.FillEllipse($bFg, ($cx - $rX), ($topY - $rY), (2 * $rX), (2 * $rY))

    # Rille nur bei 48px sinnvoll - darunter matscht sie
    if ($size -ge 48) {
        $pBg  = New-Object System.Drawing.Pen $BG, 1
        $midY = ($topY + $botY) / 2.0
        $g.DrawArc($pBg, ($cx - $rX), ($midY - $rY), (2 * $rX), (2 * $rY), 0, 180)
        $pBg.Dispose()
    }

    $bFg.Dispose(); $bShadow.Dispose()
    $g.Dispose()
    return $bmp
}

function New-Icon([int]$size) {
    if ($size -le 48) { return New-IconSmall $size } else { return New-IconLarge $size }
}

function Save-Ico([System.Drawing.Bitmap[]]$images, [string]$path) {
    # System.Drawing kann keine Multi-Res-ICOs schreiben - deshalb das
    # ICO-Format von Hand. Eingebettete PNGs sind ab Windows Vista erlaubt
    # und sparen die BMP/AND-Mask-Fummelei.
    $blobs = foreach ($img in $images) {
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    }

    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    try {
        $bw.Write([uint16]0)                  # reserved
        $bw.Write([uint16]1)                  # type: 1 = Icon
        $bw.Write([uint16]$images.Count)

        # Directory-Eintraege sind je 16 Byte; Bilddaten folgen dahinter.
        $offset = 6 + 16 * $images.Count
        for ($i = 0; $i -lt $images.Count; $i++) {
            $w = $images[$i].Width
            # 256 wird im ICO-Header als 0 kodiert.
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]0)                # Farbanzahl (0 = truecolor)
            $bw.Write([byte]0)                # reserved
            $bw.Write([uint16]1)              # color planes
            $bw.Write([uint16]32)             # bits per pixel
            $bw.Write([uint32]$blobs[$i].Length)
            $bw.Write([uint32]$offset)
            $offset += $blobs[$i].Length
        }
        foreach ($blob in $blobs) { $bw.Write($blob) }
    }
    finally {
        $bw.Dispose(); $fs.Dispose()
    }
}

$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'DTM/Assets'
$pngPath = Join-Path $assets 'dtm.png'
$icoPath = Join-Path $assets 'dtm.ico'

$master = New-Icon 256
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $pngPath (256x256)"

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$icons = foreach ($s in $sizes) { New-Icon $s }
Save-Ico $icons $icoPath
Write-Host "Wrote $icoPath (multi-res: $($sizes -join ', '))"

foreach ($i in $icons) { $i.Dispose() }
$master.Dispose()
