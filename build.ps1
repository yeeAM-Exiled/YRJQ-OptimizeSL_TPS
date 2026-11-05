[CmdletBinding()]
param(
    [string]$ServerPath,
    [string]$GameManaged,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 8 SDK or newer first: https://dotnet.microsoft.com/download'
}
if ($ServerPath -and $GameManaged) {
    throw 'Use either -ServerPath or -GameManaged.'
}
if ($ServerPath) {
    $GameManaged = Join-Path $ServerPath 'SCPSL_Data/Managed'
}
if ($GameManaged) {
    $GameManaged = (Resolve-Path -LiteralPath $GameManaged).Path
    if (-not (Test-Path -LiteralPath (Join-Path $GameManaged 'Assembly-CSharp.dll') -PathType Leaf)) {
        throw 'GameManaged must point to the SL 14.2.9 server SCPSL_Data/Managed folder.'
    }
}

$buildArgs = @('build', (Join-Path $PSScriptRoot 'YRJQ-OptimizeSL_TPS.sln'), '-c', $Configuration, '--nologo')
if ($GameManaged) {
    $buildArgs += "-p:GameManaged=$GameManaged"
}
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (exit code $LASTEXITCODE)."
}

$outputPath = Join-Path $PSScriptRoot "bin/$Configuration/net48/YRJQ-OptimizeSL_TPS.dll"
Write-Host "Plugin: $outputPath"
Write-Host 'Install the plugin DLL only. Keep 0Harmony.dll 2.2.2 in LabAPI/dependencies/global.'
