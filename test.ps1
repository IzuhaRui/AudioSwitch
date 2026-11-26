param(
    [string]$ReportPath = 'tests\unit-test-report.txt'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$tests = Join-Path $root 'tests'

Write-Host '== Adding UTF-8 BOM to C# sources =='
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
$utf8Bom = New-Object System.Text.UTF8Encoding -ArgumentList $true
Get-ChildItem -Path $src, $tests -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if (-not $hasBom) {
        $text = [System.IO.File]::ReadAllText($_.FullName, $utf8NoBom)
        [System.IO.File]::WriteAllText($_.FullName, $text, $utf8Bom)
    }
}

Write-Host '== Locating MSBuild =='
$msbuild = $null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
    if ($found) {
        $msbuild = @($found)[0]
    }
}

if (-not $msbuild) {
    $fallback = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
    if (Test-Path $fallback) {
        $msbuild = $fallback
    }
}

if (-not $msbuild) {
    throw 'MSBuild was not found. Install Visual Studio or the .NET Framework 4.x build tools.'
}

Write-Host "  MSBuild: $msbuild"
Write-Host '== Building unit tests =='
$project = Join-Path $tests 'AudioSwitch.UnitTests.csproj'
& $msbuild $project /nologo /v:minimal /p:Configuration=Release
if ($LASTEXITCODE -ne 0) {
    throw "Unit test build failed with exit code $LASTEXITCODE"
}

Write-Host '== Running unit tests =='
$exe = Join-Path $tests 'bin\Release\AudioSwitch.UnitTests.exe'
if (-not (Test-Path $exe)) {
    throw "Unit test executable not found: $exe"
}

if (-not [System.IO.Path]::IsPathRooted($ReportPath)) {
    $ReportPath = Join-Path $root $ReportPath
}

& $exe $ReportPath
$exitCode = $LASTEXITCODE
Write-Host ''
Write-Host "报告: $ReportPath"
if ($exitCode -ne 0) {
    throw "Unit tests failed with exit code $exitCode"
}