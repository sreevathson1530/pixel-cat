# Builds a self-contained Pixel into dist\Pixel (no .NET install needed) with the llama.cpp engines next to it.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\publish.ps1 [-Backends cpu,vulkan]
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\dist\Pixel'),
    [string[]]$Backends = @('cpu', 'vulkan')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'env.ps1')

$project = Join-Path $PSScriptRoot '..\src\Pixel.App\Pixel.App.csproj'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -o $Out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

foreach ($b in $Backends) {
    $src = Join-Path $PSScriptRoot "..\runtime\llama\$b"
    if (-not (Test-Path (Join-Path $src 'llama-server.exe'))) {
        Write-Warning "runtime\llama\$b missing - run scripts\setup-llama.ps1 first"
        continue
    }
    robocopy $src (Join-Path $Out "runtime\llama\$b") /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copying $b failed (robocopy $LASTEXITCODE)" }
}
# Licenses travel with the app: Pixel's own, the third-party notices and their full texts.
$repo = Join-Path $PSScriptRoot '..'
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $Out 'LICENSE.txt') -Force
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $Out -Force
robocopy (Join-Path $repo 'licenses') (Join-Path $Out 'licenses') /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "copying licenses failed (robocopy $LASTEXITCODE)" }
$global:LASTEXITCODE = 0
Write-Host "Published to $(Resolve-Path $Out)"
