[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath -ErrorAction Stop).Path

$requiredFiles = @(
    'app\OpenLimiter.exe',
    'cli\OpenLimiter.Cli.exe',
    'service\OpenLimiter.Service.exe',
    'install-service.ps1',
    'uninstall-service.ps1',
    'LICENSE',
    'README.md',
    'SECURITY.md',
    'CHANGELOG.md',
    'MANIFEST.sha256',
    'docs\ARCHITECTURE.md',
    'docs\DRIVER-DEVELOPMENT.md',
    'docs\ROADMAP.md',
    'docs\RULE-SET-FORMAT.md'
)

foreach ($relativePath in $requiredFiles) {
    $candidate = Join-Path $resolvedPackage $relativePath
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Release package is missing '$relativePath'."
    }
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    if ($ExpectedVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "ExpectedVersion '$ExpectedVersion' must use major.minor.patch format."
    }

    foreach ($relativePath in @(
        'app\OpenLimiter.exe',
        'cli\OpenLimiter.Cli.exe',
        'service\OpenLimiter.Service.exe'
    )) {
        $binaryPath = Join-Path $resolvedPackage $relativePath
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($binaryPath).ProductVersion
        if ($productVersion -ne $ExpectedVersion) {
            throw "Release binary '$relativePath' has product version '$productVersion'; expected '$ExpectedVersion'."
        }
    }
}

$forbidden = Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File |
    Where-Object {
        $_.Extension -in '.sys', '.inf', '.cat', '.cer', '.pfx' -or
        $_.Name -like '*WfpHarness*' -or
        $_.Name -like '*Test.cer*'
    }
if ($forbidden) {
    $paths = ($forbidden.FullName | Sort-Object) -join [Environment]::NewLine
    throw "Release package contains driver development artifacts:`n$paths"
}

$license = Get-Content -LiteralPath (Join-Path $resolvedPackage 'LICENSE') -Raw
if ($license -notmatch 'GNU GENERAL PUBLIC LICENSE' -or $license -notmatch 'Version 3') {
    throw 'Release package does not contain the GPL version 3 license text.'
}

$files = Get-ChildItem -LiteralPath $resolvedPackage -Recurse -File
$manifestPath = Join-Path $resolvedPackage 'MANIFEST.sha256'
$expectedFiles = $files |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object { [IO.Path]::GetRelativePath($resolvedPackage, $_.FullName).Replace('\', '/') } |
    Sort-Object
$manifestEntries = @{}
foreach ($line in Get-Content -LiteralPath $manifestPath) {
    if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') {
        throw "Release manifest contains an invalid line: '$line'."
    }

    $relativePath = $Matches[2]
    if ($manifestEntries.ContainsKey($relativePath)) {
        throw "Release manifest contains duplicate path '$relativePath'."
    }

    $manifestEntries[$relativePath] = $Matches[1]
}

$manifestFiles = @($manifestEntries.Keys | Sort-Object)
if (Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $manifestFiles) {
    throw 'Release manifest does not exactly match the packaged files.'
}

foreach ($relativePath in $expectedFiles) {
    $candidate = Join-Path $resolvedPackage $relativePath
    $actualHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $manifestEntries[$relativePath]) {
        throw "Release manifest hash mismatch for '$relativePath'."
    }
}

Write-Host "Release package check passed: $resolvedPackage"
Write-Host "Files: $($files.Count); bytes: $(($files | Measure-Object Length -Sum).Sum)"
