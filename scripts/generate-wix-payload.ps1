[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishRoot,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$resolvedPublishRoot = [IO.Path]::GetFullPath($PublishRoot)
if (-not (Test-Path -LiteralPath $resolvedPublishRoot -PathType Container)) {
    throw "Publish root '$resolvedPublishRoot' does not exist."
}

$excludedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
[void]$excludedPaths.Add([IO.Path]::GetFullPath((Join-Path $resolvedPublishRoot 'app\OpenLimiter.exe')))
[void]$excludedPaths.Add([IO.Path]::GetFullPath((Join-Path $resolvedPublishRoot 'service\OpenLimiter.Service.exe')))

function Get-StableId {
    param([string]$Prefix, [string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    $hex = [Convert]::ToHexString($hash)
    return $Prefix + $hex.Substring(0, 24)
}

$settings = [Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.Encoding = [Text.UTF8Encoding]::new($false)
$settings.NewLineChars = [Environment]::NewLine
$settings.NewLineHandling = [Xml.NewLineHandling]::Replace
$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$componentIds = [Collections.Generic.List[string]]::new()
$writer = [Xml.XmlWriter]::Create($OutputPath, $settings)
try {
    $namespace = 'http://wixtoolset.org/schemas/v4/wxs'
    $writer.WriteStartDocument()
    $writer.WriteStartElement('Wix', $namespace)

    $roots = [ordered]@{
        'INSTALLFOLDER' = $resolvedPublishRoot
        'APPFOLDER' = Join-Path $resolvedPublishRoot 'app'
        'CLIFOLDER' = Join-Path $resolvedPublishRoot 'cli'
        'SERVICEFOLDER' = Join-Path $resolvedPublishRoot 'service'
        'DOCSFOLDER' = Join-Path $resolvedPublishRoot 'docs'
    }

    foreach ($entry in $roots.GetEnumerator()) {
        $writer.WriteStartElement('Fragment', $namespace)
        $writer.WriteStartElement('DirectoryRef', $namespace)
        $writer.WriteAttributeString('Id', $entry.Key)

        $basePath = [IO.Path]::GetFullPath($entry.Value)
        $isInstallRoot = $entry.Key -eq 'INSTALLFOLDER'
        $files = Get-ChildItem -LiteralPath $basePath -File -Recurse |
            Where-Object {
                -not $excludedPaths.Contains($_.FullName) -and
                (-not $isInstallRoot -or [IO.Path]::GetDirectoryName($_.FullName) -eq $basePath)
            } |
            Sort-Object FullName

        $directories = [ordered]@{ '' = $true }
        foreach ($file in $files) {
            $relativeDirectory = [IO.Path]::GetRelativePath($basePath, $file.DirectoryName)
            if ($relativeDirectory -eq '.') {
                $relativeDirectory = ''
            }
            if (-not [string]::IsNullOrEmpty($relativeDirectory)) {
                $parts = $relativeDirectory -split '[\\/]'
                $current = ''
                foreach ($part in $parts) {
                    $current = if ([string]::IsNullOrEmpty($current)) { $part } else { "$current\$part" }
                    $directories[$current] = $true
                }
            }
        }

        function Write-DirectoryContents {
            param([string]$RelativeDirectory)

            foreach ($file in $files) {
                $fileDirectory = [IO.Path]::GetRelativePath($basePath, $file.DirectoryName)
                if ($fileDirectory -eq '.') { $fileDirectory = '' }
                if ($fileDirectory -ne $RelativeDirectory) { continue }

                $relativeFile = [IO.Path]::GetRelativePath($resolvedPublishRoot, $file.FullName).Replace('\', '/')
                $componentId = Get-StableId 'Cmp_' $relativeFile
                $fileId = Get-StableId 'Fil_' $relativeFile
                $componentIds.Add($componentId)
                $writer.WriteStartElement('Component', $namespace)
                $writer.WriteAttributeString('Id', $componentId)
                $writer.WriteAttributeString('Guid', '*')
                $writer.WriteAttributeString('Bitness', 'always64')
                $writer.WriteStartElement('File', $namespace)
                $writer.WriteAttributeString('Id', $fileId)
                $writer.WriteAttributeString('Source', $file.FullName)
                $writer.WriteAttributeString('KeyPath', 'yes')
                $writer.WriteEndElement()
                $writer.WriteEndElement()
            }

            $prefix = if ([string]::IsNullOrEmpty($RelativeDirectory)) { '' } else { "$RelativeDirectory\" }
            $children = $directories.Keys | Where-Object {
                if ([string]::IsNullOrEmpty($_) -or -not $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $false }
                $remainder = $_.Substring($prefix.Length)
                return -not $remainder.Contains('\')
            } | Sort-Object
            foreach ($child in $children) {
                $name = Split-Path -Leaf $child
                $writer.WriteStartElement('Directory', $namespace)
                $writer.WriteAttributeString('Id', (Get-StableId 'Dir_' "$($entry.Key)/$child"))
                $writer.WriteAttributeString('Name', $name)
                Write-DirectoryContents $child
                $writer.WriteEndElement()
            }
        }

        Write-DirectoryContents ''
        $writer.WriteEndElement()
        $writer.WriteEndElement()
    }

    $writer.WriteStartElement('Fragment', $namespace)
    $writer.WriteStartElement('ComponentGroup', $namespace)
    $writer.WriteAttributeString('Id', 'PayloadComponents')
    foreach ($componentId in $componentIds) {
        $writer.WriteStartElement('ComponentRef', $namespace)
        $writer.WriteAttributeString('Id', $componentId)
        $writer.WriteEndElement()
    }
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally {
    $writer.Dispose()
}

Write-Host "Generated WiX payload authoring for $($componentIds.Count) files."
