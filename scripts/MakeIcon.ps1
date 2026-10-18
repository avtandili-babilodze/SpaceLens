<#
.SYNOPSIS
    Makes src\SpaceLens\Assets\SpaceLens.ico (the exe, desktop shortcut and taskbar icon) from the in-app logo.

.DESCRIPTION
    Renders the LogoMark template in Resources\Icons\Icons.xaml (copied below; keep the two in step) at every
    icon size from 16 to 256 pixels and writes them as one PNG-compressed .ico.

.EXAMPLE
    powershell -STA -ExecutionPolicy Bypass -File scripts\MakeIcon.ps1
#>
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\src\SpaceLens\Assets\SpaceLens.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$xaml = @'
<Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Width="38" Height="38" CornerRadius="11">
  <Border.Background>
    <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
      <GradientStop Color="#38B2FF" Offset="0" />
      <GradientStop Color="#1660E0" Offset="1" />
    </LinearGradientBrush>
  </Border.Background>
  <Grid>
    <Ellipse Width="20" Height="20" Stroke="White" StrokeThickness="3.2" />
    <Ellipse Width="6" Height="6" Fill="White" Margin="11,0,0,11" />
  </Grid>
</Border>
'@

$images = foreach ($size in 16, 20, 24, 32, 40, 48, 64, 96, 128, 256) {
    $logo = [System.Windows.Markup.XamlReader]::Parse($xaml)
    # A little room around the tile, like other desktop icons; none at the smallest sizes.
    $pad = if ($size -ge 32) { $size / 32.0 } else { 0 }
    $scale = ($size - 2 * $pad) / 38.0
    $logo.LayoutTransform = New-Object System.Windows.Media.ScaleTransform($scale, $scale)
    $canvas = New-Object System.Windows.Controls.Canvas
    [System.Windows.Controls.Canvas]::SetLeft($logo, $pad)
    [System.Windows.Controls.Canvas]::SetTop($logo, $pad)
    [void]$canvas.Children.Add($logo)
    $canvas.Measure((New-Object System.Windows.Size($size, $size)))
    $canvas.Arrange((New-Object System.Windows.Rect(0, 0, $size, $size)))

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($canvas)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $png = New-Object System.IO.MemoryStream
    $encoder.Save($png)
    [pscustomobject]@{ Size = $size; Bytes = $png.ToArray() }
}

# ICONDIR, one ICONDIRENTRY per size (0 means 256), then the PNG images.
$file = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $writer.Write($image.Bytes) }
$writer.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($Out), $file.ToArray())
"Wrote $($images.Count) sizes to $Out"
