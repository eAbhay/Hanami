# Builds the single-file, self-contained Hanami.exe into dist/.
# Usage: powershell -ExecutionPolicy Bypass -File tools\build.ps1 [-SkipTests]
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$dist = Join-Path $repo "dist"

if (-not $SkipTests) {
    dotnet test --project (Join-Path $repo "src\Hanami.Tests\Hanami.Tests.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

dotnet publish (Join-Path $repo "src\Hanami.App\Hanami.App.csproj") -c Release -o (Join-Path $repo "src\Hanami.App\bin\publish") --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $repo "src\Hanami.App\bin\publish\Hanami.exe") (Join-Path $dist "Hanami.exe") -Force
$size = [math]::Round((Get-Item (Join-Path $dist "Hanami.exe")).Length / 1MB, 1)
Write-Host "dist\Hanami.exe ($size MB)"
