param([switch]$Verify)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
foreach ($name in @('Windows.Foundation', 'Windows.Devices', 'Windows.Networking', 'Windows.Storage')) {
    $references += '/r:' + (Join-Path $env:WINDIR "System32\WinMetadata\$name.winmd")
}
foreach ($name in @('System.Runtime', 'System.Collections', 'System.Runtime.InteropServices.WindowsRuntime', 'System.Threading.Tasks')) {
    $assembly = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$name") -Recurse -Filter '*.dll' | Select-Object -First 1
    if (!$assembly) { throw "Required Windows runtime assembly missing: $name" }
    $references += '/r:' + $assembly.FullName
}
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$PSScriptRoot\PeripheralBattery.exe" "/win32manifest:$PSScriptRoot\src\app.manifest" "/win32icon:$PSScriptRoot\assets\app.ico" $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
if ($Verify) {
    foreach ($test in @('DiscoveryRegression', 'AppearanceRegression', 'TooltipRegression')) {
        $testFile = Join-Path ([IO.Path]::GetTempPath()) ("peripheral-check-" + [Guid]::NewGuid() + '.exe')
        try {
            & $compiler /nologo /target:exe "/main:$test" "/out:$testFile" "/win32manifest:$PSScriptRoot\src\app.manifest" $references $sources (Join-Path $PSScriptRoot "tests\$test.cs")
            if ($LASTEXITCODE -ne 0) { throw "$test build failed" }
            & $testFile
            if ($LASTEXITCODE -ne 0) { throw "$test failed" }
        }
        finally { if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile } }
    }
}
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'PeripheralBattery.exe') | Select-Object Name, Length
