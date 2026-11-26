param([string]$OutputPath = 'PadDesk.exe')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /win32icon:assets/PadDesk.ico /resource:assets/PadDesk.ico,PadDesk.ico "/out:$OutputPath" /r:System.Runtime.Serialization.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xaml.dll "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" Model.cs Engine.cs Device.cs NativeOutput.cs ControllerLayout.cs ControllerView.cs App.cs Dialogs.cs RemoteInput.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
