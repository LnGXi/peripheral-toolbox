$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$PSScriptRoot\PeripheralBattery.exe" "/win32manifest:$PSScriptRoot\src\app.manifest" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'PeripheralBattery.exe') | Select-Object Name, Length
