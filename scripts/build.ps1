# Builds the stopwatch exe with the .NET Framework 4.8 C# compiler.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$refs = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
$source = Join-Path $root "src\Stopwatch2.cs"
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "app.ico"

# Output file name in Russian: "Секундомер.exe" (built from code points to stay encoding safe).
$name = -join ([int[]](0x0421, 0x0435, 0x043A, 0x0443, 0x043D, 0x0434, 0x043E, 0x043C, 0x0435, 0x0440) | ForEach-Object { [char]$_ })
$output = Join-Path $root ($name + ".exe")

if (-not (Test-Path $icon)) {
    & (Join-Path $PSScriptRoot "make-icon.ps1")
}

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$output" `
    "/win32icon:$icon" `
    "/win32manifest:$manifest" `
    "/r:$refs\PresentationFramework.dll" `
    "/r:$refs\PresentationCore.dll" `
    "/r:$refs\WindowsBase.dll" `
    "/r:$refs\System.Xaml.dll" `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Output ("Built: " + $output)
