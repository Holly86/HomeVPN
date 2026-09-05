$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Output = Join-Path $Root 'artifacts\HomeVPN-win-x64'

dotnet restore (Join-Path $Root 'HomeVPN.sln')
dotnet build (Join-Path $Root 'HomeVPN.sln') -c Release --no-restore
dotnet publish (Join-Path $Root 'src\HomeVpn.App\HomeVpn.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Output

Write-Host "Built: $(Join-Path $Output 'HomeVPN.exe')"
