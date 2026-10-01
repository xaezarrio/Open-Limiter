# OpenLimiter

OpenLimiter is an early Windows network-control project intended to become an
open-source alternative for per-application blocking and bandwidth policies.

## What works in the current foundation

- Validated rules for executable paths
- Application-scoped inbound and outbound Windows Firewall rules
- Per-application upload throttling through Windows Policy-based QoS
- Non-elevated GUI and CLI policy changes through a Windows service
- Named-pipe access restricted to the user SID selected during installation
- Transactional rule persistence, startup restoration, and JSONL policy audit
- Service health reports the WFP driver API, callout count, and classification count
- GUI and CLI views for bounded flow identity, direction, protocol, and endpoint events
- A native WFP harness that installs temporary IPv4 and IPv6 inspection filters
- A service-owned dynamic WFP session that removes its filters automatically on exit
- Process grouping by executable path, with instance counts and searchable PIDs
- One updateable policy per executable path across the GUI, CLI, and service
- Persistent Pause and Resume controls that remove or restore enforcement without deleting a rule
- Live per-process upload and download throughput from Windows ETW, with honest unavailable states
- Native system tray controls for opening the app and pausing or resuming saved policies
- Temporary rules that expire and remove their Windows enforcement even when the GUI is closed
- Named policy profiles with transactional activation and local-time recurring schedules
- Self-contained x64 MSI installation for the GUI, CLI, and policy service
- Measured TCP connection and bound UDP endpoint inventory with owning PID
- Protected instances join a matching named executable only when that path is unambiguous
- Process enumeration that tolerates protected and short-lived processes
- Atomic JSON rule persistence

## Important limitation

Per-application download throttling is not implemented. Windows Firewall can block
traffic and Policy-based QoS can throttle outbound traffic, but full bidirectional
shaping requires a WFP callout driver. Download limiting is deferred to the project
backlog and is not part of the active release plan. A fail-open, pass-through WFP driver and
dynamic-session test harness now build from source. When the driver is present, the
policy service installs the same temporary flow inspection filters and reports that
session separately from driver availability. The driver is intentionally unsigned
and uninstalled on the development workstation. It records bounded flow identity
events but has no shaping queues. The application does not expose a download-limit
control that it cannot yet enforce.

## Build and test

```powershell
dotnet restore
dotnet build OpenLimiter.slnx --configuration Release
dotnet test OpenLimiter.slnx --configuration Release
```

The CLI can inspect processes without the service. Connection output is grouped by
executable path by default; use the detailed view for individual TCP and UDP endpoints:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- processes
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- connections
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- connections --view detailed
```

Run the desktop application:

```powershell
dotnet run --project src\OpenLimiter.App\OpenLimiter.App.csproj
```

After installing the policy service, block an executable in both directions or
limit its upload rate from a normal terminal:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- apply --path C:\Path\app.exe --block both
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- apply --path C:\Path\app.exe --upload 2.5mbps
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- apply --path C:\Path\app.exe --block out --duration 10m
```

List saved rule IDs, then pause or resume a rule without losing its block and
upload-limit configuration:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- rules
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- pause --id <rule-guid>
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- resume --id <rule-guid>
```

Paused rules remain in the rule store and stay paused after a service or machine
restart. Resume reapplies the saved Firewall and outbound QoS configuration.
Temporary rules accept `m`, `h`, or `d` units up to seven days. Their saved rule
and active Firewall/QoS enforcement are removed automatically at expiry.

Capture the current policy set as a profile, activate it atomically, and schedule
it using the PC's local clock:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- profile-save --name Work
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- profiles
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- profile-activate --id <profile-guid>
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- schedule-add --name "Weekday work" --profile <profile-guid> --days weekdays --time 09:00
```

The GUI and CLI do not need elevation. The LocalSystem policy service validates and
applies their requests. Do not test policy enforcement against a process that
provides your only remote access path.

Inspect both service and driver availability with:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- service-status
```

Read the most recent flow identities held by the driver with:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- driver-flows
```

Export the complete rule set to a versioned JSON document, or replace the saved
rule set from a previous export:

```powershell
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- export --file .\openlimiter-rules.json
dotnet run --project src\OpenLimiter.Cli\OpenLimiter.Cli.csproj -- import --file .\openlimiter-rules.json --confirm replace-all
```

Export refuses to overwrite a file unless `--confirm overwrite` is supplied.
Import validates the entire document before changing policy and restores the
previous rule set if enforcement or persistence fails. The CLI also refuses to
use the service's internal `%ProgramData%\OpenLimiter\rules.json` as an interchange
file; keep exports in a separate user-controlled location. The complete schema is
documented in `docs/RULE-SET-FORMAT.md`.

Create a framework-dependent release folder with:

```powershell
.\scripts\publish.ps1 -Runtime win-x64
```

That package requires the .NET 10 Desktop Runtime on the target PC. Add
`-SelfContained` to create a larger package that carries its own runtime. The
publish script also creates a ZIP and matching `.sha256` file beside the folder.
The package itself contains `MANIFEST.sha256`, which covers every other packaged
file and is validated before the ZIP is created.

Build the self-contained x64 MSI with:

```powershell
.\scripts\build-msi.ps1
```

The resulting `artifacts\installer\OpenLimiter-0.5.0-win-x64.msi` installs the
GUI, CLI, Start Menu shortcut, and auto-starting LocalSystem policy service. It
authorizes the Windows account that launched setup and carries the .NET runtime.
Run the MSI as an administrator. Uninstall stops and removes the service and cleans
the Firewall/QoS enforcement owned by saved OpenLimiter rules; configuration under
`%ProgramData%\OpenLimiter` is retained for a later reinstall.

If `OpenLimiterPolicy` was installed by an older release script, close the GUI and
run the MSI as administrator. Setup stops and reconfigures that same service name as
an MSI-managed service while retaining `%ProgramData%\OpenLimiter`. Use the matching
release's `uninstall-service.ps1 -KeepPolicies` first only if Windows reports that the
legacy service is marked for deletion, then rerun the MSI after the service disappears.

Install the published service from an elevated PowerShell window:

```powershell
.\artifacts\publish\OpenLimiter-0.5.0-win-x64\install-service.ps1 `
  -ServiceBinaryPath .\artifacts\publish\OpenLimiter-0.5.0-win-x64\service\OpenLimiter.Service.exe
```

The installer authorizes the Windows SID that launched it. Rules and audit entries
are stored under `%ProgramData%\OpenLimiter`. Service binaries are copied to an
administrator-only directory under `%ProgramFiles%\OpenLimiter`; the LocalSystem
service never runs from the downloaded package directory. To remove the service and
clean active OpenLimiter policies, run the published `uninstall-service.ps1` as
administrator.

The GUI disables policy changes when its release version does not match the installed
policy service. Re-run the installer from the same release, then select Check service.

## Project status

This is pre-release software. The privileged service, live ETW throughput, temporary
rules, profiles, schedules, system tray, self-contained MSI, WFP pass-through source,
test harness, and bounded flow telemetry are implemented. Download shaping, VM
stability qualification for shaping, and public driver signing remain in the backlog. See
`docs/ARCHITECTURE.md` and `docs/ROADMAP.md` before contributing.

## License

OpenLimiter is licensed under the GNU General Public License v3.0 only
(`GPL-3.0-only`). See `LICENSE` for the complete terms. Contributions are accepted
under the same license.

Release candidates must pass `docs/RELEASE-CHECKLIST.md`. Current changes are listed
in `CHANGELOG.md`.

## Driver development

Download shaping is in the project backlog. The existing WFP research foundation is
documented in `docs/DRIVER-DEVELOPMENT.md`. Run
`scripts\check-driver-toolchain.ps1` before building kernel code. Build the unsigned
driver and native harness with `scripts\build-driver.ps1`; they are excluded from
release packages, and the driver must only be loaded in a recoverable test VM.
