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
$render=New-Object Windows.Media.Imaging.RenderTargetBitmap 256,256,96,96,([Windows.Media.PixelFormats]::Pbgra32); $render.Render($visual)
$encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($render))
$stream=New-Object IO.MemoryStream
$encoder.Save($stream)
$png=$stream.ToArray()
$stream.Dispose()
$writer=New-Object IO.BinaryWriter ([IO.File]::Create("$PSScriptRoot\TokenStats.ico")); foreach($n in @(0,1,1)){$writer.Write([uint16]$n)}; foreach($n in @(0,0,0,0)){$writer.Write([byte]$n)};$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$png.Length);$writer.Write([uint32]22);$writer.Write($png);$writer.Dispose()
