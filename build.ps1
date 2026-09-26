# Builds TCNet-dotnet in Debug or Release.
#   ./build.ps1                     Debug build + tests (library, tests, monitor)
#   ./build.ps1 -c Release          Release build + tests
#   ./build.ps1 -c Release -App     also build the MAUI app for this OS
#   ./build.ps1 -c Both             Debug and Release
param(
    [Alias('c')][ValidateSet('Debug', 'Release', 'Both')][string]$Configuration = 'Debug',
    [switch]$App,
    [switch]$NoTest
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$configs = if ($Configuration -eq 'Both') { @('Debug', 'Release') } else { @($Configuration) }

foreach ($cfg in $configs) {
    Write-Host "== $cfg ==" -ForegroundColor Cyan
    dotnet build src/TCNet/TCNet.csproj -c $cfg; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet build tools/TCNet.Monitor/TCNet.Monitor.csproj -c $cfg; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet build tests/TCNet.Tests/TCNet.Tests.csproj -c $cfg; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if (-not $NoTest) {
        dotnet test tests/TCNet.Tests/TCNet.Tests.csproj -c $cfg --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
    if ($App) {
        $tfm = if ($IsMacOS) { 'net10.0-maccatalyst' } elseif ($IsLinux) { 'net10.0-android' } else { 'net10.0-windows10.0.19041.0' }
        dotnet build samples/TCNet.Maui/TCNet.Maui.csproj -c $cfg -f $tfm "-p:TCNetAppTfm=$tfm"; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
}
Write-Host "Done." -ForegroundColor Green
