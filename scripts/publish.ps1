[CmdletBinding()]
param(
    [string]$Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
    $Version = [string]$buildProperties.Project.PropertyGroup.VersionPrefix
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Release version '$Version' must use major.minor.patch format."
}

$publishRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\publish'))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $publishRoot "OpenLimiter-$Version-$Runtime"))
if (-not $outputRoot.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved publish directory escaped the repository artifact root.'
}
if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

dotnet publish (Join-Path $repositoryRoot 'src\OpenLimiter.App\OpenLimiter.App.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained:$SelfContained `
    -p:Version=$Version `
    --output (Join-Path $outputRoot 'app')

dotnet publish (Join-Path $repositoryRoot 'src\OpenLimiter.Cli\OpenLimiter.Cli.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained:$SelfContained `
    -p:Version=$Version `
    --output (Join-Path $outputRoot 'cli')

dotnet publish (Join-Path $repositoryRoot 'src\OpenLimiter.Service\OpenLimiter.Service.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained:$SelfContained `
    -p:Version=$Version `
    --output (Join-Path $outputRoot 'service')

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install-service.ps1') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\uninstall-service.ps1') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'SECURITY.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'CHANGELOG.md') -Destination $outputRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs') -Destination $outputRoot -Recurse

$manifestPath = Join-Path $outputRoot 'MANIFEST.sha256'
$manifestLines = Get-ChildItem -LiteralPath $outputRoot -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [IO.Path]::GetRelativePath($outputRoot, $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$relativePath"
    }
[IO.File]::WriteAllLines($manifestPath, $manifestLines, [Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot 'check-release-package.ps1') `
    -PackagePath $outputRoot `
    -ExpectedVersion $Version

$archivePath = Join-Path $publishRoot "OpenLimiter-$Version-$Runtime.zip"
$checksumPath = "$archivePath.sha256"
foreach ($artifactPath in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $artifactPath) {
        Remove-Item -LiteralPath $artifactPath -Force
    }
}
Compress-Archive -Path (Join-Path $outputRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $checksumPath,
    "$archiveHash *$([IO.Path]::GetFileName($archivePath))$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

Write-Host "Published OpenLimiter to $outputRoot"
Write-Host "Archive: $archivePath"
Write-Host "SHA-256: $archiveHash"
