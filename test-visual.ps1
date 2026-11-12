param([string]$Executable = 'artifacts/preview/PadDesk.exe')
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$folder = Split-Path -Parent $exe
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /platform:x64 "/out:$folder\VisualTests.exe" "/r:$exe" /r:System.Xaml.dll /r:System.Runtime.Serialization.dll "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" (Join-Path $PSScriptRoot 'VisualTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Visual test compilation failed' }
& (Join-Path $folder 'VisualTests.exe') $exe
if ($LASTEXITCODE -ne 0) { throw 'Visual behavior checks failed' }
