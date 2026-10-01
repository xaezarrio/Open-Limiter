# WFP driver development

The WFP callout is a separate kernel component. It is not required for application
blocking or outbound Policy-based QoS. Download-rate limiting is in the project
backlog and is not part of the active release plan. The current pass-through driver
and test harness remain as a research foundation. OpenLimiter must not enable
download-rate controls unless that work resumes and the driver is built, signed,
installed, and exercised on supported Windows versions.

## Prerequisites

- Visual Studio 2022 with the Desktop development with C++ workload
- A Windows Driver Kit version compatible with the installed SDK and Visual Studio
- A Windows 11 virtual machine dedicated to test signing and Driver Verifier
- A kernel debugger or recoverable VM snapshot before the first driver load

Run `scripts\check-driver-toolchain.ps1` before creating or building the driver.
The check must find `fwpsk.h`, `ntddk.h`, the kernel-driver MSBuild targets, and
the x64 SignTool.

Build the current pass-through milestone without installing it:

```powershell
.\scripts\build-driver.ps1 -Configuration Release
```

The unsigned output is written under `artifacts\driver\x64\Release`. The build
verifies the primitive-driver INF and generates a catalog beside the packaged INF
and SYS. WDK stamping is configured to preserve the reviewed `DriverVer` from the
source INF so repeated builds do not invent a time-based version. These are
development artifacts only and must not be installed on a workstation. Loading,
test signing, and WFP filter registration belong in a recoverable test VM.

When the driver is eventually running, `OpenLimiter.Service` opens
`\\.\OpenLimiterWfp` as LocalSystem and validates the exact 32-byte capability
response. It also validates the exact 4,624-byte bounded flow snapshot before
exposing either result through policy-service protocol version 6.
The current driver contract is API 1.3. User mode rejects a different major
version, an older minor version, missing telemetry flags, or fewer than two
registered callouts before it creates any WFP policy objects.

## Disposable VM workflow

Do not run these scripts on a daily-use or remote-only machine. In a disposable
Windows VM, disable Secure Boot, enable Windows test signing with
`bcdedit /set testsigning on`, reboot, take a snapshot, and then run from an
elevated PowerShell window:

```powershell
.\scripts\build-driver.ps1 -Configuration Debug
.\scripts\test-sign-driver.ps1 -ConfirmTestVm
.\scripts\install-test-driver.ps1 -ConfirmTestVm
& .\artifacts\driver\x64\Debug\OpenLimiter.WfpHarness.exe
dotnet run --project src\OpenLimiter.Cli -- service-status
.\scripts\uninstall-test-driver.ps1 -ConfirmTestVm
```

Each mutating script refuses to run without `-ConfirmTestVm`. Installation also
rejects an invalid signature and rolls back the service entry and copied binary
when driver startup fails. Uninstall removes the exact driver service, binary, and
self-issued OpenLimiter test certificates from the VM. Test signing signs the SYS,
regenerates the catalog against that signed binary, and signs the catalog as a
separate package artifact.

For an end-to-end disposable-VM check, the guarded smoke script builds and signs
the Debug driver, loads it, starts the policy service console host, creates a local
TCP flow, verifies that the CLI receives a driver event, and then removes the
driver and test certificates:

```powershell
.\scripts\run-driver-vm-smoke.ps1 -ConfirmTestVm
```

The script keeps its service logs under `artifacts\driver\vm-smoke-*`. It does not
replace Driver Verifier, sleep/resume, upgrade, or uninstall testing.

The native harness first validates the driver's versioned IOCTL, then atomically
adds IPv4 and IPv6 `ALE_FLOW_ESTABLISHED` callouts and inspection filters in a
dynamic WFP session. It never blocks traffic. Closing the harness, pressing Ctrl+C,
or terminating its process removes every object owned by that session. While it is
running, the harness polls the driver once per second and prints new flow sequence,
PID, IP version, protocol, direction, local and remote ports, and application
identity hash values.

The policy service uses the same dynamic provider, sublayer, callout, and filter
keys in normal operation. It first verifies that the driver is available, then
creates every WFP object in one transaction. Driver availability and successful
filter activation are reported separately through the CLI and GUI. The native
harness remains useful for testing the driver without installing the service, but
it must not run at the same time because both sessions intentionally use the same
object keys.

## First driver milestone

The first loadable driver is pass-through only. Its current source:

1. Register IPv4 and IPv6 callouts and unregister them cleanly.
2. Count classification events without delaying, cloning, modifying, or dropping traffic.
3. Store the latest 64 flow identities and endpoint tuples in a fixed ring protected
   by a spin lock.
4. Expose versioned capability and flow-snapshot IOCTLs to the policy service.
5. Restrict the Release control device to LocalSystem. Debug builds also allow
   administrators so the native VM harness can validate the development driver.
6. Remain fail-open and return `FWP_ACTION_CONTINUE` whenever invoked.

It deliberately does not create persistent WFP callouts or filters yet, so a built
binary does not receive traffic by itself. Byte accounting and repeated load/unload
testing remain VM milestones.

Rate queues are not part of this milestone. Adding scheduling before lifecycle and
flow cleanup are proven would turn ordinary defects into machine-wide connectivity
failures.

## Shaping milestone

After pass-through stability, add bounded token buckets keyed by the service-owned
rule ID. Application identity is captured at ALE flow establishment and associated
with transport or datagram processing. TCP and UDP/QUIC require distinct queue and
back-pressure policies. Every queue needs byte, packet, and age limits.

Classify callbacks must not block or perform unbounded work. Deferred packets need
explicit ownership, cancellation, reinjection, and flow-delete behavior. If a queue
cannot be maintained safely, traffic is permitted and the failure is reported to
the service instead of silently disconnecting the machine.

## Trust boundary

- Only the LocalSystem policy service may open the driver control device.
- IOCTL structures carry an API version and exact byte size.
- Unknown flags, IDs, lengths, and operation codes are rejected.
- The driver accepts structured rules, never command text or executable paths.
- Path-to-rule resolution remains in the service; the driver consumes stable rule
  and flow identifiers.
- The installer owns driver start, upgrade, rollback, and removal.

## Release gate

A public driver release requires x64 and ARM64 builds, HVCI compatibility, Driver
Verifier runs, clean uninstall with no stale WFP objects, crash-dump review, and
Microsoft signing. Test-signed development binaries must never be placed in the
normal application release directory.

## References

- [Creating a primitive driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/develop/creating-a-primitive-driver)
- [WFP object management](https://learn.microsoft.com/en-us/windows/win32/fwp/object-management)
- [WFP best practices](https://learn.microsoft.com/en-us/windows/win32/fwp/best-practices)
- [FwpmEngineOpen0](https://learn.microsoft.com/en-us/windows/win32/api/fwpmu/nf-fwpmu-fwpmengineopen0)
