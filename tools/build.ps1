# Builds the single-file, self-contained Hanami.exe into dist/, with dist/Hanami.exe.sha256 beside it.
# Usage: powershell -ExecutionPolicy Bypass -File tools\build.ps1 [-SkipTests] [-Version 1.2.3]
#   -Version  stamps the exe (a leading "v" is dropped, so a tag name works); without it the csproj's version is used.
param([switch]$SkipTests, [string]$Version = "")
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = Join-Path $repo "dist"

$versionArgs = @()
if ($Version -ne "") {
    $Version = $Version -replace '^v', ''
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
    $versionArgs = @("-p:Version=$Version")
}

if (-not $SkipTests) {
    dotnet test --project (Join-Path $repo "src\Hanami.Tests\Hanami.Tests.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

dotnet publish (Join-Path $repo "src\Hanami.App\Hanami.App.csproj") -c Release -o (Join-Path $repo "src\Hanami.App\bin\publish") --nologo @versionArgs
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

New-Item -ItemType Directory -Force $dist | Out-Null
$exe = Join-Path $dist "Hanami.exe"
Copy-Item (Join-Path $repo "src\Hanami.App\bin\publish\Hanami.exe") $exe -Force
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
# "<hash> *<file>" is the format `sha256sum -c` reads; Get-FileHash prints the same hash.
[IO.File]::WriteAllText((Join-Path $dist "Hanami.exe.sha256"), "$hash *Hanami.exe`n", [Text.Encoding]::ASCII)
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "dist\Hanami.exe ($size MB)"
Write-Host "SHA-256 $hash"
