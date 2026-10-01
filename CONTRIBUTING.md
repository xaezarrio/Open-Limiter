# Contributing

OpenLimiter is at an early stage. Small, testable changes are easier to review than
large feature bundles.

## Local setup

1. Install the .NET 10 SDK on Windows 10 or 11.
2. Run `dotnet restore`.
3. Run `dotnet build OpenLimiter.slnx --configuration Release`.
4. Run `dotnet test OpenLimiter.slnx --configuration Release`.

Or run the same managed-code and PowerShell checks used by CI:

```powershell
.\scripts\verify.ps1
```

Contributors with the WDK toolchain can include the native Release build and INF
verification with `.\scripts\verify.ps1 -IncludeDriver`.

WFP driver work additionally requires Visual Studio, the Windows Driver Kit, a
test-signing environment, and a dedicated test machine or virtual machine.
Run `scripts\check-driver-toolchain.ps1` before submitting driver changes.

## Project boundaries

- Keep domain models and validation in `OpenLimiter.Core` platform-neutral.
- Keep Windows Firewall, QoS, process, and WFP code in Windows projects.
- Do not run the GUI elevated permanently when a service can own the privileged work.
- Never show invented throughput, connection counts, or enforcement success.
- Every system policy must have a stable owner name and a cleanup path.
- New UI controls need loading, empty, error, keyboard-focus, and disabled states.

## Pull requests

Include the problem, the approach, tests, and manual Windows verification. Driver
changes must describe Windows versions, architectures, HVCI state, sleep/resume,
upgrade, and uninstall results.

## Contribution license

By submitting a contribution, you agree to license it under GPL-3.0-only, the same
license used by this repository.
