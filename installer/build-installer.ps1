# Builds a standalone (self-contained) Windows x64 install of TCNet Monitor.
#   ./installer/build-installer.ps1                 tests, publish, portable zip, Setup.exe
#   ./installer/build-installer.ps1 -SkipTests
#   ./installer/build-installer.ps1 -NoSetup        portable zip only (no Inno Setup needed)
#   ./installer/build-installer.ps1 -c Debug        publish a Debug build instead of Release
#
# Output: artifacts\installer\
#   TCNet-Monitor-<ver>-win-x64-portable.zip   unzip anywhere and run TCNet.Maui.exe
#   TCNet-Monitor-Setup-<ver>-win-x64.exe      installer (Start menu, uninstaller, PATH, firewall)
# Neither needs .NET or the Windows App SDK on the target PC.
param(
    [Alias('c')][ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$NoSetup
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$tfm = 'net10.0-windows10.0.19041.0'
$rid = 'win-x64'
$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }
$pub = Join-Path $root 'artifacts\publish'
$appDir = Join-Path $pub 'app'
$cliDir = Join-Path $pub 'cli'
$outDir = Join-Path $root 'artifacts\installer'

function Step($m) { Write-Host "== $m ==" -ForegroundColor Cyan }
function Check { if ($LASTEXITCODE) { throw "Step failed (exit $LASTEXITCODE)" } }

Step "TCNet Monitor $version ($Configuration, $rid)"
foreach ($d in $pub, $outDir) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $outDir | Out-Null

if (-not $SkipTests) {
    Step 'Tests'
    dotnet test tests/TCNet.Tests/TCNet.Tests.csproj -c $Configuration; Check
}

Step 'Publish app (self-contained, unpackaged)'
dotnet publish samples/TCNet.Maui/TCNet.Maui.csproj -c $Configuration -f $tfm -r $rid `
    "-p:TCNetAppTfm=$tfm" -p:SelfContained=true -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o $appDir; Check
if (-not (Test-Path (Join-Path $appDir 'TCNet.Maui.exe'))) { throw 'TCNet.Maui.exe missing from publish output' }

Step 'Publish tcnet-monitor CLI (single file)'
dotnet publish tools/TCNet.Monitor/TCNet.Monitor.csproj -c $Configuration -r $rid `
    -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PackAsTool=false -o $cliDir; Check
if (-not (Test-Path (Join-Path $cliDir 'tcnet-monitor.exe'))) { throw 'tcnet-monitor.exe missing from publish output' }

Step 'Portable zip'
$stage = Join-Path $pub 'portable'
Copy-Item $appDir $stage -Recurse
New-Item -ItemType Directory -Force (Join-Path $stage 'cli') | Out-Null
Copy-Item (Join-Path $cliDir 'tcnet-monitor.exe') (Join-Path $stage 'cli')
Copy-Item README.md, LICENSE $stage
$zip = Join-Path $outDir "TCNet-Monitor-$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "  $zip"

if (-not $NoSetup) {
    Step 'Setup.exe (Inno Setup)'
    function Find-Iscc {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
        $bases = @("$env:LOCALAPPDATA\Programs", ${env:ProgramFiles(x86)}, $env:ProgramFiles) | Where-Object { $_ }
        foreach ($b in $bases) {
            $hit = Get-ChildItem $b -Directory -Filter 'Inno Setup*' -ErrorAction SilentlyContinue |
                ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
            if ($hit) { return $hit }
        }
        return $null
    }
    $iscc = Find-Iscc
    if (-not $iscc -and (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Host '  Inno Setup not found; installing it for this user with winget...'
        winget install -e --id JRSoftware.InnoSetup --scope user --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
        $iscc = Find-Iscc
    }
    if (-not $iscc) {
        Write-Warning 'Inno Setup (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php and re-run; the portable zip is ready.'
    } else {
        Write-Host "  ISCC: $iscc"
        $isccArgs = @("/DAppVersion=$version", "/DAppDir=$appDir", "/DCliDir=$cliDir", "/DOutDir=$outDir")
        $ico = Get-ChildItem samples/TCNet.Maui/obj -Recurse -Filter 'appicon.ico' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($ico) { $isccArgs += "/DAppIcon=$($ico.FullName)" }
        & $iscc @isccArgs (Join-Path $PSScriptRoot 'TCNetMonitor.iss'); Check
    }
}

Step 'Done'
Get-ChildItem $outDir | ForEach-Object { '{0,-50} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
