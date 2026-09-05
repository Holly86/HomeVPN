$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$geometry = [Windows.Media.Geometry]::Parse('M32,3 L57,13 55,35 Q52,51 32,61 Q12,51 9,35 L7,13 Z M17,29 L21,33 24,30 24,46 30,46 30,36 36,36 36,46 42,46 42,30 45,33 49,29 33,15 Z')
$brush = [Windows.Media.BrushConverter]::new().ConvertFromString('#16834f')
$sizes = @(16,24,32,48,256)
$images = @()
foreach ($size in $sizes) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([Windows.Media.ScaleTransform]::new($size/64.0,$size/64.0))
    $drawing.DrawGeometry($brush,$null,$geometry)
    $drawing.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new(); $encoder.Save($stream)
    $bytes = $stream.ToArray(); $images += ,$bytes
    [IO.File]::WriteAllBytes("$root/assets/HomeVPN-$size.png",$bytes); $stream.Dispose()
}
$output = [IO.File]::Create("$root/assets/HomeVPN.ico")
$writer = [IO.BinaryWriter]::new($output)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0;$i -lt $sizes.Count;$i++) {
    $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }; $writer.Dispose()
