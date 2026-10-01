[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ServiceBinaryPath,

    [string]$AllowedUserSid
)

$ErrorActionPreference = 'Stop'
$serviceName = 'OpenLimiterPolicy'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this installer from an elevated PowerShell session.'
}

$resolvedBinary = (Resolve-Path -LiteralPath $ServiceBinaryPath).Path
if ([IO.Path]::GetFileName($resolvedBinary) -ne 'OpenLimiter.Service.exe') {
    throw 'ServiceBinaryPath must point to OpenLimiter.Service.exe.'
}

$programFiles = [IO.Path]::GetFullPath([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))
$installRoot = [IO.Path]::GetFullPath((Join-Path $programFiles 'OpenLimiter'))
$serviceDirectory = [IO.Path]::GetFullPath((Join-Path $installRoot 'Service'))
$stagingDirectory = [IO.Path]::GetFullPath((Join-Path $installRoot "Service.stage-$PID"))
$backupDirectory = [IO.Path]::GetFullPath((Join-Path $installRoot "Service.backup-$PID"))
foreach ($path in @($installRoot, $serviceDirectory, $stagingDirectory, $backupDirectory)) {
    if (-not ($path.Equals($installRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith($installRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
        throw 'Resolved service installation path escaped the OpenLimiter Program Files directory.'
    }
}

function Set-OpenLimiterInstallAcl {
    param([Parameter(Mandatory)][string]$Path)

    $directoryInfo = [IO.Directory]::CreateDirectory($Path)
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true, $false)
    $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagation = [Security.AccessControl.PropagationFlags]::None
    foreach ($sidType in @(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid
    )) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sidType, $null),
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance,
            $propagation,
            [Security.AccessControl.AccessControlType]::Allow))
    }
    [IO.FileSystemAclExtensions]::SetAccessControl($directoryInfo, $security)
}

Set-OpenLimiterInstallAcl -Path $installRoot
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
Set-OpenLimiterInstallAcl -Path $stagingDirectory
$sourceDirectory = Split-Path -Parent $resolvedBinary
Get-ChildItem -LiteralPath $sourceDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stagingDirectory -Recurse -Force
}
$stagedBinary = Join-Path $stagingDirectory 'OpenLimiter.Service.exe'
if (-not (Test-Path -LiteralPath $stagedBinary -PathType Leaf)) {
    throw 'The staged service directory does not contain OpenLimiter.Service.exe.'
}

if ([string]::IsNullOrWhiteSpace($AllowedUserSid)) {
    $AllowedUserSid = $identity.User.Value
}
$null = [Security.Principal.SecurityIdentifier]::new($AllowedUserSid)

$dataDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'OpenLimiter'
$directoryInfo = [IO.Directory]::CreateDirectory($dataDirectory)
$directorySecurity = [Security.AccessControl.DirectorySecurity]::new()
$directorySecurity.SetAccessRuleProtection($true, $false)
$inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
$propagation = [Security.AccessControl.PropagationFlags]::None
$directorySecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid, $null),
    [Security.AccessControl.FileSystemRights]::FullControl,
    $inheritance,
    $propagation,
    [Security.AccessControl.AccessControlType]::Allow))
$directorySecurity.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid, $null),
    [Security.AccessControl.FileSystemRights]::FullControl,
    $inheritance,
    $propagation,
    [Security.AccessControl.AccessControlType]::Allow))
[IO.FileSystemAclExtensions]::SetAccessControl($directoryInfo, $directorySecurity)

$sidPath = Join-Path $dataDirectory 'allowed-user.sid'
$sidStagePath = Join-Path $dataDirectory "allowed-user.sid.stage-$PID"
$sidRollbackPath = Join-Path $dataDirectory "allowed-user.sid.rollback-$PID"
$sidPreviouslyExisted = Test-Path -LiteralPath $sidPath -PathType Leaf
$previousSidBytes = if ($sidPreviouslyExisted) { [IO.File]::ReadAllBytes($sidPath) } else { $null }

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existingService) {
    if (-not (Test-Path -LiteralPath $serviceDirectory -PathType Container)) {
        throw 'The existing service has no managed Program Files directory. Repair or remove it before upgrading.'
    }
    if ($existingService.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force
        $existingService.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }
}

if (Test-Path -LiteralPath $backupDirectory) {
    Remove-Item -LiteralPath $backupDirectory -Recurse -Force
}
$oldDirectoryMoved = $false
$newServiceCreated = $false
$sidUpdated = $false
try {
    [IO.File]::WriteAllText($sidStagePath, $AllowedUserSid, [Text.Encoding]::ASCII)
    [IO.File]::Move($sidStagePath, $sidPath, $true)
    $sidUpdated = $true

    if (Test-Path -LiteralPath $serviceDirectory) {
        Move-Item -LiteralPath $serviceDirectory -Destination $backupDirectory
        $oldDirectoryMoved = $true
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $serviceDirectory
    $installedBinary = Join-Path $serviceDirectory 'OpenLimiter.Service.exe'

    if ($null -ne $existingService) {
        $serviceConfigArguments = @(
            'config'
            $serviceName
            'binPath='
            "`"$installedBinary`""
            'start='
            'auto'
            'obj='
            'LocalSystem'
        )
        $result = & sc.exe @serviceConfigArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Could not update the Windows service: $result"
        }
    }
    else {
        New-Service `
            -Name $serviceName `
            -BinaryPathName "`"$installedBinary`"" `
            -DisplayName 'OpenLimiter Policy Service' `
            -Description 'Validates and enforces OpenLimiter firewall and QoS policies.' `
            -StartupType Automatic | Out-Null
        $newServiceCreated = $true
    }

    $result = & sc.exe failure $serviceName 'reset= 86400' 'actions= restart/5000/restart/15000/none/0'
    if ($LASTEXITCODE -ne 0) {
        throw "Could not configure service recovery: $result"
    }

    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(20))

    if ($oldDirectoryMoved) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}
catch {
    $installFailure = $_
    if ($sidUpdated) {
        try {
            if ($sidPreviouslyExisted) {
                [IO.File]::WriteAllBytes($sidRollbackPath, $previousSidBytes)
                [IO.File]::Move($sidRollbackPath, $sidPath, $true)
            }
            elseif (Test-Path -LiteralPath $sidPath -PathType Leaf) {
                Remove-Item -LiteralPath $sidPath -Force
            }
        }
        catch {
            Write-Warning "The previous authorized SID could not be restored: $($_.Exception.Message)"
        }
    }
    if ($newServiceCreated) {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $serviceName 2>$null | Out-Null
    }
    if (Test-Path -LiteralPath $serviceDirectory) {
        Remove-Item -LiteralPath $serviceDirectory -Recurse -Force
    }
    if ($oldDirectoryMoved -and (Test-Path -LiteralPath $backupDirectory)) {
        Move-Item -LiteralPath $backupDirectory -Destination $serviceDirectory
        try {
            Start-Service -Name $serviceName
        }
        catch {
            Write-Warning "The previous service files were restored, but the service could not be restarted: $($_.Exception.Message)"
        }
    }
    throw $installFailure
}
finally {
    foreach ($temporarySidPath in @($sidStagePath, $sidRollbackPath)) {
        if (Test-Path -LiteralPath $temporarySidPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporarySidPath -Force
        }
    }
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}

Write-Host "OpenLimiter Policy Service is running for SID $AllowedUserSid."
