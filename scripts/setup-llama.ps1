# Downloads llama.cpp Windows builds into runtime\llama\<backend>\ (git-ignored).
# Usage: powershell -File scripts\setup-llama.ps1 [-Build b11476] [-Backends cpu,vulkan,sycl,openvino]
param(
    [string]$Build = 'b11476',
    [string[]]$Backends = @('cpu', 'vulkan', 'sycl', 'openvino')
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Join-Path $PSScriptRoot '..\runtime\llama'
New-Item -ItemType Directory -Force $root | Out-Null

$assets = @{
    cpu      = "llama-$Build-bin-win-cpu-x64.zip"
    vulkan   = "llama-$Build-bin-win-vulkan-x64.zip"
    sycl     = "llama-$Build-bin-win-sycl-x64.zip"
    openvino = "llama-$Build-bin-win-openvino-2026.4.1-x64.zip"
}

foreach ($b in $Backends) {
    $dest = Join-Path $root $b
    if (Test-Path (Join-Path $dest 'llama-server.exe')) { Write-Host "$b already present"; continue }
    $zip = Join-Path $root "$b.zip"
    $url = "https://github.com/ggml-org/llama.cpp/releases/download/$Build/$($assets[$b])"
    Write-Host "Downloading $b from $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    New-Item -ItemType Directory -Force $dest | Out-Null
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    Remove-Item $zip
    # Some archives nest binaries one folder deep; flatten so llama-server.exe sits in $dest.
    $server = Get-ChildItem $dest -Recurse -Filter 'llama-server.exe' | Select-Object -First 1
    if ($server -and $server.DirectoryName -ne (Resolve-Path $dest).Path) {
        Get-ChildItem $server.DirectoryName | Move-Item -Destination $dest -Force
    }
    Set-Content (Join-Path $dest 'BUILD.txt') $Build
    Write-Host "$b ready"
}
