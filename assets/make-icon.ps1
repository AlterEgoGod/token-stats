Add-Type -AssemblyName PresentationCore,WindowsBase
$visual = New-Object Windows.Media.DrawingVisual
$dc=$visual.RenderOpen()
$dc.DrawRoundedRectangle([Windows.Media.Brushes]::Transparent,$null,[Windows.Rect]::new(0,0,256,256),48,48)
$bg=New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(29,29,29))
$dc.DrawRoundedRectangle($bg,$null,[Windows.Rect]::new(8,8,240,240),52,52)
$dc.PushTransform([Windows.Media.TranslateTransform]::new(52,52)); $dc.PushTransform([Windows.Media.ScaleTransform]::new(6.3,6.3))
$brush=New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(0,157,235))
foreach($path in (Get-Content (Join-Path $PSScriptRoot 'reicon-bolt-paths.json') -Raw | ConvertFrom-Json)) { $geometry=[Windows.Media.Geometry]::Parse($path); $dc.DrawGeometry($brush,$null,$geometry) }
$dc.Pop();$dc.Pop();$dc.Close()
# Store native frames for Explorer, shortcuts and the taskbar.
$frames = @()
foreach ($size in @(16,20,24,32,40,48,64,96,128,256)) {
    $scaled = New-Object Windows.Media.DrawingVisual
    $context = $scaled.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($size / 256.0, $size / 256.0))
    $context.DrawDrawing($visual.Drawing)
    $context.Pop()
    $context.Close()
    $render = New-Object Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([Windows.Media.PixelFormats]::Pbgra32)
    $render.Render($scaled)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($render))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    $frames += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
}
$writer = New-Object IO.BinaryWriter ([IO.File]::Create("$PSScriptRoot\TokenStats.ico"))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
