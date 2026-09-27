# Builds and tests the TCNet library.
#   ./build.ps1                 Debug build + tests
#   ./build.ps1 -c Release      Release build + tests
#   ./build.ps1 -c Both         Debug and Release
#   ./build.ps1 -c Release -Pack   also writes the NuGet package to artifacts/
param(
    [Alias('c')][ValidateSet('Debug', 'Release', 'Both')][string]$Configuration = 'Debug',
    [switch]$NoTest,
    [switch]$Pack
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$configs = if ($Configuration -eq 'Both') { @('Debug', 'Release') } else { @($Configuration) }

foreach ($cfg in $configs) {
    Write-Host "== $cfg ==" -ForegroundColor Cyan
    dotnet build TCNet.slnx -c $cfg; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if (-not $NoTest) {
        dotnet test TCNet.slnx -c $cfg --no-build; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
    if ($Pack) {
        dotnet pack src/TCNet/TCNet.csproj -c $cfg --no-build -o artifacts; if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
}
Write-Host "Done." -ForegroundColor Green
