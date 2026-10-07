# Installs the .NET 10 SDK for the current user only (no admin rights needed).
$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$script = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing
& $script -Channel 10.0 -InstallDir $installDir -NoPath
& (Join-Path $installDir 'dotnet.exe') --list-sdks
