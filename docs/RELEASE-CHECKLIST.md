# Release checklist

## Managed application

- Run `.\scripts\verify.ps1 -IncludeDriver` on the supported Windows SDK and WDK.
- Run `dotnet list OpenLimiter.slnx package --vulnerable --include-transitive`.
- Render the GUI at 1440 x 1000 and 1000 x 650 with the policy service connected.
- Verify process grouping, search, policy apply/pause/resume/remove, paused-state persistence after service restart, service-offline recovery,
  service-version mismatch blocking and recovery, driver-offline state, and
  driver-flow refresh.
- Confirm the service executable runs from `%ProgramFiles%\OpenLimiter\Service` and
  a nonadministrator cannot modify that directory.
- Run `.\scripts\publish.ps1 -Version <version> -Runtime win-x64` and keep the
  release-package check enabled.
- Sign the GUI, CLI, service, and installer with the release certificate.

## Driver VM gate

- Create a fresh disposable Windows 11 VM snapshot with Secure Boot state recorded.
- Run `.\scripts\run-driver-vm-smoke.ps1 -ConfirmTestVm` from elevated PowerShell.
- Repeat on HVCI off and HVCI on configurations.
- Run Driver Verifier against `OpenLimiterWfp` through load, traffic, unload, and reboot.
- Exercise TCP, UDP, IPv4, IPv6, loopback, sleep/resume, service restart, driver
  upgrade, and uninstall.
- Confirm the dynamic WFP provider, sublayer, callouts, and filters disappear when
  the service or harness exits unexpectedly.
- Review kernel crash dumps and service logs. Do not waive unexplained verifier,
  WFP, or cleanup failures.
- Build x64 and ARM64 packages, run InfVerif and Inf2Cat, and verify catalog contents.
- Replace test signatures with Microsoft-signed release artifacts.

## Publication

- Update `CHANGELOG.md`, `README.md`, version properties, and screenshots.
- Confirm GPL-3.0-only identifiers and the full `LICENSE` file are present.
- Confirm the normal application archive contains no `.sys`, `.inf`, `.cat`,
  `.cer`, `.pfx`, or WFP harness file.
- Verify `MANIFEST.sha256` covers every packaged file, then publish the archive
  checksum and immutable source and binary archives.
- Enable the repository private security-advisory form before announcing the release.
