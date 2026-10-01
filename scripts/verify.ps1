[CmdletBinding()]
param(
    [switch]$IncludeDriver
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$parseErrors = [Collections.Generic.List[object]]::new()
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' | ForEach-Object {
    $tokens = $null
    $fileErrors = $null
    [Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$fileErrors) | Out-Null
    foreach ($error in $fileErrors) {
        $parseErrors.Add($error)
    }
}
if ($parseErrors.Count -gt 0) {
    $parseErrors | Format-List
    throw 'One or more PowerShell scripts contain syntax errors.'
}

dotnet restore (Join-Path $repositoryRoot 'OpenLimiter.slnx')
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

dotnet build (Join-Path $repositoryRoot 'OpenLimiter.slnx') --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

dotnet test (Join-Path $repositoryRoot 'OpenLimiter.slnx') --configuration Release --no-build
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

dotnet format (Join-Path $repositoryRoot 'OpenLimiter.slnx') --verify-no-changes --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "dotnet format verification failed with exit code $LASTEXITCODE."
}

if ($IncludeDriver) {
    & (Join-Path $PSScriptRoot 'build-driver.ps1') -Configuration Release
}

Write-Host 'OpenLimiter verification passed.'
