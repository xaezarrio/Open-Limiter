[CmdletBinding()]
param(
    [string]$DriverPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\driver\x64\Debug\OpenLimiter.Wfp.sys'),
    [switch]$ConfirmTestVm
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmTestVm) {
    throw 'Refusing to change certificate stores. Re-run inside a disposable test VM with -ConfirmTestVm.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell window inside the test VM.'
}

try {
    if (Confirm-SecureBootUEFI -ErrorAction Stop) {
        throw 'Secure Boot is enabled. Use a disposable VM configured for Windows driver test signing.'
    }
}
catch [PlatformNotSupportedException] {
}

$resolvedDriver = (Resolve-Path -LiteralPath $DriverPath -ErrorAction Stop).Path
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$configuration = Split-Path -Leaf (Split-Path -Parent $resolvedDriver)
$packageDirectory = Join-Path $repositoryRoot "artifacts\driver\x64\$configuration\OpenLimiter.Wfp"
$packageDriver = Join-Path $packageDirectory 'OpenLimiter.Wfp.sys'
$catalogPath = Join-Path $packageDirectory 'openlimiter.wfp.cat'
$toolchainScript = Join-Path $PSScriptRoot 'check-driver-toolchain.ps1'
& $toolchainScript

$certificateSubject = 'CN=OpenLimiter WFP Test Driver'
$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $certificateSubject `
    -CertStoreLocation 'Cert:\LocalMachine\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddMonths(3)

$certificateDirectory = Join-Path $repositoryRoot 'artifacts\driver\test-certificate'
New-Item -ItemType Directory -Force -Path $certificateDirectory | Out-Null
$certificatePath = Join-Path $certificateDirectory 'OpenLimiter.Wfp.Test.cer'
Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null
Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null

$signTool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
    -Filter 'signtool.exe' `
    -Recurse |
    Where-Object FullName -Match '\\x64\\signtool\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($signTool)) {
    throw 'The x64 SignTool executable was not found.'
}

& $signTool sign /v /fd SHA256 /s My /sm /sha1 $certificate.Thumbprint $resolvedDriver
if ($LASTEXITCODE -ne 0) {
    throw "SignTool failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath $resolvedDriver -Destination $packageDriver -Force
$inf2Cat = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' `
    -Filter 'inf2cat.exe' `
    -Recurse |
    Where-Object FullName -Match '\\x86\\inf2cat\.exe$' |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($inf2Cat)) {
    throw 'Inf2Cat was not found.'
}

& $inf2Cat "/driver:$packageDirectory" /os:10_X64
if ($LASTEXITCODE -ne 0) {
    throw "Inf2Cat failed with exit code $LASTEXITCODE."
}

& $signTool sign /v /fd SHA256 /s My /sm /sha1 $certificate.Thumbprint $catalogPath
if ($LASTEXITCODE -ne 0) {
    throw "Catalog signing failed with exit code $LASTEXITCODE."
}

& $signTool verify /v /kp $resolvedDriver
if ($LASTEXITCODE -ne 0) {
    throw "Kernel policy signature verification failed with exit code $LASTEXITCODE."
}
& $signTool verify /v /kp $catalogPath
if ($LASTEXITCODE -ne 0) {
    throw "Driver catalog signature verification failed with exit code $LASTEXITCODE."
}

Write-Host "Test-signed driver: $resolvedDriver"
Write-Host "Test-signed package catalog: $catalogPath"
Write-Host "Test certificate: $certificatePath"
