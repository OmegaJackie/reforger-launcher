# Builds ReforgerLauncher.exe (one file, needs the .NET 10 Desktop Runtime) and puts it in the folder above, next to the
# config*.json files it works on. -Out <folder> publishes somewhere else instead (e.g. for a test).
param([string]$Out = (Split-Path $PSScriptRoot))
$ErrorActionPreference = "Stop"
$publish = Join-Path $PSScriptRoot "bin\publish"
if (-not (Test-Path (Join-Path $PSScriptRoot "launcher.ico"))) { & (Join-Path $PSScriptRoot "make-icon.ps1") }
dotnet publish (Join-Path $PSScriptRoot "ReforgerLauncher.csproj") -c Release -o $publish --nologo
if ($LASTEXITCODE) { throw "dotnet publish failed" }
New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item (Join-Path $publish "ReforgerLauncher.exe") (Join-Path $Out "ReforgerLauncher.exe") -Force
"Built $(Join-Path $Out 'ReforgerLauncher.exe') ($([int]((Get-Item (Join-Path $Out 'ReforgerLauncher.exe')).Length / 1KB)) KB)"
