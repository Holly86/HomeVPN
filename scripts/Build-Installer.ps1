$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
Push-Location $root
try {
    & "$PSScriptRoot/Get-Runtime.ps1"
    dotnet restore HomeVPN.sln
    if ($LASTEXITCODE) { throw 'Restore failed' }
    dotnet build HomeVPN.sln -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    dotnet test HomeVPN.sln -c Release --no-build
    if ($LASTEXITCODE) { throw 'Tests failed' }
    foreach ($project in @('HomeVpn.App','HomeVpn.TunnelService')) {
        $out = if ($project -eq 'HomeVpn.App') { 'artifacts/HomeVPN' } else { 'artifacts/HomeVPN/Runtime/x64' }
        dotnet publish "src/$project/$project.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $out
        if ($LASTEXITCODE) { throw 'Publish failed' }
    }
    Copy-Item LICENSE artifacts/HomeVPN/licenses/HomeVPN.txt -Force
    Copy-Item installer/WiX-LICENSE.txt artifacts/HomeVPN/licenses/WiX.txt -Force
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'WiX tool restore failed' }
    dotnet wix extension add WixToolset.BootstrapperApplications.wixext/5.0.2
    if ($LASTEXITCODE) { throw 'WiX bootstrapper extension restore failed' }
    dotnet wix extension add WixToolset.UI.wixext/5.0.2
    if ($LASTEXITCODE) { throw 'WiX UI extension restore failed' }
    dotnet wix build installer/Package.wxs -arch x64 -culture de-de -ext WixToolset.UI.wixext -o artifacts/installer/HomeVPN.msi
    if ($LASTEXITCODE) { throw 'MSI build failed' }
    dotnet wix build installer/Bundle.wxs -arch x64 -ext WixToolset.BootstrapperApplications.wixext -o artifacts/installer/HomeVPN-Setup-win-x64.exe
    if ($LASTEXITCODE) { throw 'Bootstrapper build failed' }
} finally { Pop-Location }
