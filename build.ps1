param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Portable
)

$ErrorActionPreference = 'Stop'
$localDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$systemDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
$dotnetPath = $null

foreach ($candidate in @($localDotnet, $systemDotnet)) {
    if ($candidate -and (Test-Path $candidate)) {
        $installedSdks = & $candidate --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $installedSdks -match '^9\.') {
            $dotnetPath = $candidate
            break
        }
    }
}

if (-not $dotnetPath) {
    throw '.NET 9 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/9.0'
}

$arguments = @(
    'publish',
    (Join-Path $PSScriptRoot 'BTChargeIndicator.csproj'),
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--output', (Join-Path $PSScriptRoot 'dist'),
    '-p:PublishSingleFile=true'
)

if ($Portable) {
    $arguments += '--no-self-contained'
} else {
    $arguments += '--self-contained'
    $arguments += '-p:EnableCompressionInSingleFile=true'
}

& $dotnetPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Ready: $(Join-Path $PSScriptRoot 'dist\BTChargeIndicator.exe')"
