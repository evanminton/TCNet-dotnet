# Builds and tests TCNet: library, tests, the tcnet utility and (with -App) the MAUI app.
#   ./build.ps1                      Debug build + tests
#   ./build.ps1 -c Release           Release build + tests
#   ./build.ps1 -c Both              Debug and Release
#   ./build.ps1 -c Both -App         also builds the MAUI app for this OS (Windows, Mac Catalyst, or Android on Linux)
#   ./build.ps1 -c Release -Pack     also writes TCNet and TCNet.Utility NuGet packages to artifacts/
#   ./build.ps1 -NoTest              skip the tests
param(
    [Alias('c')][ValidateSet('Debug', 'Release', 'Both')][string]$Configuration = 'Debug',
    [switch]$App,
    [switch]$NoTest,
    [switch]$Pack
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$configs = if ($Configuration -eq 'Both') { @('Debug', 'Release') } else { @($Configuration) }
$projects = @('src/TCNet/TCNet.csproj', 'tests/TCNet.Tests/TCNet.Tests.csproj', 'tools/TCNet.Utility/TCNet.Utility.csproj')
# Windows PowerShell 5.1 has no $IsWindows/$IsMacOS/$IsLinux and only runs on Windows.
$onWindows = $PSVersionTable.PSEdition -ne 'Core' -or $IsWindows
$appTfm = if ($onWindows) { 'net10.0-windows10.0.19041.0' } elseif ($IsMacOS) { 'net10.0-maccatalyst' } else { 'net10.0-android' }

function Run([string[]]$cmd) {
    Write-Host "> dotnet $($cmd -join ' ')" -ForegroundColor DarkGray
    # Failure is detected through the exit code; with 'Stop', stderr output of a redirected native
    # command would be turned into a terminating NativeCommandError before the code is checked.
    $ErrorActionPreference = 'Continue'
    & dotnet @cmd
    if ($LASTEXITCODE) { Write-Host "FAILED: dotnet $($cmd -join ' ')" -ForegroundColor Red; exit $LASTEXITCODE }
}

foreach ($cfg in $configs) {
    Write-Host "== $cfg ==" -ForegroundColor Cyan
    foreach ($p in $projects) { Run @('build', $p, '-c', $cfg, '-nologo', '-v', 'minimal') }
    if (-not $NoTest) { Run @('test', 'tests/TCNet.Tests/TCNet.Tests.csproj', '-c', $cfg, '--no-build', '-nologo') }
    if ($App) {
        if ($onWindows -or $IsMacOS -or (& dotnet workload list 2>$null | Select-String -Quiet '\bandroid\b')) {
            Run @('build', 'app/TCNet.Maui/TCNet.Maui.csproj', '-c', $cfg, "-p:TCNetAppTfm=$appTfm", '-nologo', '-v', 'minimal')
        }
        else { Write-Host "Skipping the MAUI app: on Linux it builds only for Android (dotnet workload install maui-android)." -ForegroundColor Yellow }
    }
    if ($Pack) {
        Run @('pack', 'src/TCNet/TCNet.csproj', '-c', $cfg, '--no-build', '-o', 'artifacts')
        Run @('pack', 'tools/TCNet.Utility/TCNet.Utility.csproj', '-c', $cfg, '--no-build', '-o', 'artifacts')
    }
}
Write-Host "Done." -ForegroundColor Green
