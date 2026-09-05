param([string]$OutputDirectory = "$PSScriptRoot/../artifacts/HomeVPN/Runtime/x64")
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$cache = Join-Path $root 'runtime-cache'
New-Item -ItemType Directory -Force $cache,$OutputDirectory | Out-Null
function Fetch($name, $url, $hash) {
    $file = Join-Path $cache $name
    if (!(Test-Path -LiteralPath $file)) { Invoke-WebRequest $url -OutFile $file }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "SHA256 mismatch: $name" }
    return $file
}
$commit = '4e6726c23ae9c5cb58e0c9910f3b7515621d133d'
$source = Join-Path $root 'vendor/wireguard-windows'
if (!(Test-Path "$source/.git")) { git clone https://git.zx2c4.com/wireguard-windows $source; if ($LASTEXITCODE) { throw 'Clone failed' } }
if ((git -C $source rev-parse HEAD) -ne $commit) { git -C $source fetch origin $commit; git -C $source checkout --detach $commit }
if ((git -C $source rev-parse HEAD) -ne $commit -or (git -C $source status --porcelain --untracked-files=no)) { throw 'Upstream checkout must match pin without modifications' }
$go = Fetch 'go.zip' 'https://download.wireguard.com/windows-toolchain/distfiles/go1.26.2-windows_amd64_2026-04-20.zip' '79b5d5ff6f2718dfe25e199cda314d174ecf5bb19f0f2d777aec089d09a12620'
$llvm = Fetch 'llvm.zip' 'https://download.wireguard.com/windows-toolchain/distfiles/llvm-mingw-20260311-ucrt-x86_64.zip' 'dd4c67d98959479c7be2fb6709ba074475991590848cb9d0eb2620be06b182e1'
$nt = Fetch 'wireguard-nt-1.1.zip' 'https://download.wireguard.com/wireguard-nt/wireguard-nt-1.1.zip' 'dceb30a9bc4be48cce0f74160fc88a585a2c2627366e8f846fc6658f9038dace'
foreach ($item in @(@($go,'go'),@($llvm,'llvm'),@($nt,'nt'))) {
    $dest = Join-Path $cache $item[1]
    if (!(Test-Path "$dest/.complete")) {
        New-Item -ItemType Directory -Force $dest | Out-Null
        tar -xf $item[0] -C $dest --strip-components 1
        if ($LASTEXITCODE) { throw 'Extraction failed' }
        Set-Content "$dest/.complete" 'verified'
    }
}
$env:GOROOT = "$cache/go"
$env:GOTOOLCHAIN = 'local'
$env:PATH = "$cache/go/bin;$cache/llvm/bin;$env:PATH"
$env:GOOS = 'windows'; $env:GOARCH = 'amd64'; $env:CGO_ENABLED = '1'
$env:CC = 'x86_64-w64-mingw32-gcc'
$env:CGO_CFLAGS = '-O3 -Wall -Wno-unused-function -Wno-switch -std=gnu11 -DWINVER=0x0A00'
$output = [IO.Path]::GetFullPath($OutputDirectory)
Push-Location "$source/embeddable-dll-service"
try {
    go mod verify
    if ($LASTEXITCODE) { throw 'Go dependency verification failed' }
    go build -buildmode c-shared -ldflags='-w -s' -trimpath -buildvcs=false -o "$output/tunnel.dll"
    if ($LASTEXITCODE) { throw 'Native build failed' }
} finally { Pop-Location }
Copy-Item "$cache/nt/bin/amd64/wireguard.dll" "$output/wireguard.dll" -Force
$hashes = @{}
foreach ($name in @('tunnel.dll','wireguard.dll')) { $hashes[$name] = (Get-FileHash "$output/$name" -Algorithm SHA256).Hash }
$pinFile = "$PSScriptRoot/../native-hashes.json"
if (Test-Path $pinFile) {
    $pins = Get-Content $pinFile -Raw | ConvertFrom-Json
    foreach ($name in $hashes.Keys) { if ($pins.$name -ne $hashes[$name]) { throw "Native output differs from reviewed pin: $name" } }
}
$hashes | ConvertTo-Json | Set-Content "$output/native-hashes.json"
$licenses = [IO.Path]::GetFullPath("$output/../../licenses")
New-Item -ItemType Directory -Force $licenses | Out-Null
Copy-Item "$source/COPYING" "$licenses/WireGuard-Windows.txt" -Force
Copy-Item "$cache/nt/LICENSE.txt" "$licenses/WireGuardNT.txt" -Force
Write-Output $hashes
