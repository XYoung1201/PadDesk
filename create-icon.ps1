$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'assets') | Out-Null
$bitmap=New-Object Drawing.Bitmap 256,256
$g=[Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode='AntiAlias'
$blue=[Drawing.ColorTranslator]::FromHtml('#5365e9')
$brush=New-Object Drawing.SolidBrush $blue
$path=New-Object Drawing.Drawing2D.GraphicsPath
foreach($arc in @(@(8,8,100,100,180,90),@(148,8,100,100,270,90),@(148,148,100,100,0,90),@(8,148,100,100,90,90))){$path.AddArc($arc[0],$arc[1],$arc[2],$arc[3],$arc[4],$arc[5])}
$path.CloseFigure();$g.FillPath($brush,$path)
$body=New-Object Drawing.Drawing2D.GraphicsPath
$body.AddBezier(62,80,40,80,35,130,28,164)
$body.AddBezier(28,164,20,202,50,208,88,166)
$body.AddLine(88,166,164,166)
$body.AddBezier(164,166,202,208,236,195,221,161)
$body.AddBezier(221,161,210,110,210,80,186,80)
$body.CloseFigure();$g.FillPath([Drawing.Brushes]::White,$body)
$g.FillRectangle($brush,64,110,14,42);$g.FillRectangle($brush,50,124,42,14)
$g.FillEllipse($brush,167,103,18,18);$g.FillEllipse($brush,185,123,18,18)
$points=[Drawing.Point[]]@([Drawing.Point]::new(122,122),[Drawing.Point]::new(122,218),[Drawing.Point]::new(143,197),[Drawing.Point]::new(158,226),[Drawing.Point]::new(179,215),[Drawing.Point]::new(163,188),[Drawing.Point]::new(193,184))
$ink=New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#16243d'))
$g.FillPolygon($ink,$points);$pen=New-Object Drawing.Pen $blue,5;$pen.LineJoin='Round';$g.DrawPolygon($pen,$points)
$bitmap.Save((Join-Path $PSScriptRoot 'assets/PadDesk.png'),[Drawing.Imaging.ImageFormat]::Png)
$sizes=@(16,20,24,32,48,64,128,256);$images=@()
foreach($size in $sizes){$small=New-Object Drawing.Bitmap $size,$size;$sg=[Drawing.Graphics]::FromImage($small);$sg.InterpolationMode='HighQualityBicubic';$sg.DrawImage($bitmap,0,0,$size,$size);$stream=New-Object IO.MemoryStream;$small.Save($stream,[Drawing.Imaging.ImageFormat]::Png);$images+=,@($stream.ToArray());$sg.Dispose();$small.Dispose();$stream.Dispose()}
$file=[IO.File]::Create((Join-Path $PSScriptRoot 'assets/PadDesk.ico'));$writer=New-Object IO.BinaryWriter $file
$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count);$offset=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++){$size=$sizes[$i]%256;$writer.Write([byte]$size);$writer.Write([byte]$size);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$i].Count);$writer.Write([uint32]$offset);$offset+=$images[$i].Count}
foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
$writer.Dispose();$file.Dispose();$g.Dispose();$bitmap.Dispose();$path.Dispose();$body.Dispose();$brush.Dispose();$ink.Dispose();$pen.Dispose()
