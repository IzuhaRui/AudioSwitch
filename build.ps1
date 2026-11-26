param(
    [string]$Configuration = 'Release',
    [string]$DistDirectory = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'

Write-Host '== Adding UTF-8 BOM to C# sources =='
$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
$utf8Bom = New-Object System.Text.UTF8Encoding -ArgumentList $true
Get-ChildItem -Path $src -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    if (-not $hasBom) {
        $text = [System.IO.File]::ReadAllText($_.FullName, $utf8NoBom)
        [System.IO.File]::WriteAllText($_.FullName, $text, $utf8Bom)
        Write-Host "  + $($_.Name)"
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

Write-Host '== Building =='
$project = Join-Path $src 'AudioSwitch.csproj'
& $msbuild $project /nologo /v:minimal /p:Configuration=$Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$outputDir = Join-Path $src "bin\$Configuration"
$distDir = Join-Path $root $DistDirectory
if (Test-Path $distDir) {
    try {
        Remove-Item $distDir -Recurse -Force -ErrorAction Stop
    } catch {
        throw "无法更新 $DistDirectory：里面的 AudioSwitch.exe 正在运行。请先右键托盘图标 -> 退出，然后重新编译。"
    }
}
New-Item -ItemType Directory -Path $distDir | Out-Null

Copy-Item (Join-Path $outputDir 'AudioSwitch.exe') $distDir
$configFile = Join-Path $outputDir 'AudioSwitch.exe.config'
if (Test-Path $configFile) {
    Copy-Item $configFile $distDir
}

$readmeSource = Join-Path $root '使用说明.txt'
if (Test-Path $readmeSource) {
    $readmeText = [System.IO.File]::ReadAllText($readmeSource, $utf8NoBom)
    $readmeText = $readmeText -replace "`r?`n", "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $distDir '使用说明.txt'), $readmeText, $utf8Bom)
}

$quickSource = Join-Path $root '使用说明-极简版.txt'
if (Test-Path $quickSource) {
    $quickText = [System.IO.File]::ReadAllText($quickSource, $utf8NoBom)
    $quickText = $quickText -replace "`r?`n", "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $distDir '使用说明-极简版.txt'), $quickText, $utf8Bom)
}

Write-Host ''
Write-Host '== Done =='
Get-ChildItem $distDir | ForEach-Object {
    Write-Host ("  {0}  ({1:N1} KB)" -f $_.Name, ($_.Length / 1KB))
}
