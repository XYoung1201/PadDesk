param([switch]$LogicOnly)
$ErrorActionPreference = 'Stop'
if (-not $LogicOnly -and (Get-Process PadDesk -ErrorAction SilentlyContinue)) { throw '请先从托盘退出 PadDesk，再运行输入测试；或使用 -LogicOnly 仅测试逻辑。' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDir = Join-Path ([System.IO.Path]::GetTempPath()) ('PadDesk-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SDL2.dll') -Destination $testDir
$model = Join-Path $PSScriptRoot 'Model.cs'
$engine = Join-Path $PSScriptRoot 'Engine.cs'
$device = Join-Path $PSScriptRoot 'Device.cs'
$native = Join-Path $PSScriptRoot 'NativeOutput.cs'
& $compiler /nologo /platform:x64 /r:System.Runtime.Serialization.dll "/out:$testDir\Tests.exe" $model $engine (Join-Path $PSScriptRoot 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& (Join-Path $testDir 'Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Behavior tests failed' }
if ($LogicOnly) { return }
& $compiler /nologo /platform:x64 /r:System.Runtime.Serialization.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$testDir\IntegrationTests.exe" $model $engine $device $native (Join-Path $PSScriptRoot 'IntegrationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Integration compilation failed' }
& (Join-Path $testDir 'IntegrationTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed' }
