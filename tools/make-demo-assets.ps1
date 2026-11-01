# Generates placeholder library content for development (devroot/Library).
# Real anime wallpapers get imported through the app; these are just for testing.
param(
    [string]$Root = (Join-Path $PSScriptRoot "..\devroot")
)
$ErrorActionPreference = "Stop"
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { throw "ffmpeg not found on PATH (winget install Gyan.FFmpeg)" }

function New-DemoVideo($id, $name, $filter, $seconds) {
    $dir = Join-Path $Root "Library\wallpapers\$id"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $out = Join-Path $dir "media.mp4"
    & $ffmpeg -y -loglevel error -f lavfi -i $filter -t $seconds `
        -c:v libx264 -preset veryfast -crf 24 -pix_fmt yuv420p -movflags +faststart $out
    $manifest = [ordered]@{
        id = $id; name = $name; type = "video"; file = "media.mp4"
        createdAt = (Get-Date).ToUniversalTime().ToString("o")
        width = 1920; height = 1080; durationSec = $seconds; hasThumbnail = $false
    }
    $manifest | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $dir "manifest.json")
    Write-Host "created $out"
}

New-DemoVideo "demo-aurora" "Demo - aurora gradient" `
    "gradients=s=1920x1080:r=30:c0=0x1b1035:c1=0x6a2c70:c2=0xf08a5d:c3=0x3e8ed0:nb_colors=4:speed=0.008:type=radial:seed=7" 20
New-DemoVideo "demo-sea" "Demo - teal drift" `
    "gradients=s=1920x1080:r=30:c0=0x0b3954:c1=0x087e8b:c2=0xbfd7ea:nb_colors=3:speed=0.01:type=spiral:seed=3" 20
