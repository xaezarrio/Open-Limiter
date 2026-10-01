# Changelog

All notable changes to OpenLimiter will be recorded in this file. The project uses
semantic versioning once public releases begin.

## 0.5.0 - 2026-10-01

### Added

- GUI and CLI detection for application and policy-service version mismatches
- Executable-path process grouping with instance and PID search
- Idempotent CLI updates and service-side duplicate-rule rejection per executable
- Inbound and outbound application blocking through Windows Firewall
- Outbound application rate limits through Windows Policy-based QoS
- LocalSystem policy service with SID-restricted, versioned named-pipe IPC
- Transactional rule persistence, rollback, startup restoration, and JSONL audit
- Rule-store replacements use unique write-through temporary files and clean cancellation debris
- Startup validation that skips invalid or duplicate stored executable rules
- WPF rule editor with explicit loading, empty, unavailable, and error states
- Pass-through x64 WFP callout driver API 1.3
- Service-owned dynamic IPv4 and IPv6 flow inspection filters
- Bounded kernel flow telemetry with PID, identity hash, direction, protocol, and endpoints
- Native WFP harness and guarded disposable-VM smoke scripts
- Primitive-driver INF verification and catalog generation
- Windows CI for managed projects and PowerShell syntax
- Per-file SHA-256 release manifest with exact package coverage validation
- Release checks verify GUI, CLI, and service ProductVersion metadata
- Release packages include the documentation referenced by their README
- Structured GitHub bug, feature, and pull-request contribution templates
- Versioned CLI rule-set import/export with strict parsing and compensating rollback
- PID-owned IPv4 and IPv6 TCP connection and bound UDP endpoint inventory
- Executable-grouped connection summaries with an optional per-endpoint CLI view
- Service-side WFP application identity association with saved rules
- Protocol version 4 flow events with local and remote endpoint tuples
- GUI and CLI Pause and Resume actions that retain saved rule configuration
- Live per-process TCP and UDP throughput sampling through kernel ETW
- Native system tray with global and per-rule Pause and Resume actions
- Persistent temporary rules with automatic service-side expiry cleanup
- Reusable policy profiles with transactional activation
- Recurring local-time profile schedules with delayed-start catch-up and once-per-day state
- Self-contained x64 MSI with Start Menu shortcut and managed policy-service lifecycle
- MSI uninstall cleanup for OpenLimiter-owned Firewall and QoS enforcement

### Changed

- Download limiting and bidirectional shaping moved from the active roadmap to the backlog
- The disabled download-rate control now identifies the feature as backlog work instead of implying that installing a driver would enable it
- The GUI blocks policy mutations until its release version matches the installed service
- Saved policies now provide confirmed per-rule removal and a confirmed Remove all action
- Rule listings show Active or Paused state, and paused rules remain disabled across restart
- Updating a paused rule keeps it paused until the user explicitly resumes it
- Profile snapshots normalize paused and temporary rules into reusable active, permanent templates
- The service protocol is version 6 for traffic, profile, and schedule operations

### Fixed

- Release binaries keep the declared semantic ProductVersion when built from Git or CI
- Policy mutations now use a dedicated 45-second response window instead of the
  3-second service-connection timeout, preventing normal Windows QoS operations from
  being reported as an unavailable policy service
- Existing service upgrades pass `sc.exe config` option names and values as separate
  arguments, as required by the Windows Service Control utility
- PowerShell QoS failures are decoded into readable text instead of exposing serialized
  CLIXML progress and error records in the GUI
- Removing a QoS policy that does not exist now succeeds, allowing first-time rules to
  pass the idempotent cleanup step before enforcement

### Security

- LocalSystem service binaries install under an administrator-only Program Files directory
- Service upgrades stage new binaries and restore the previous installation if
  configuration or startup fails
- Service upgrade rollback restores the previously authorized user SID with the binary
- Policy mutations restore their previous enforcement state if a client request is
  cancelled after Windows policy changes begin
- Completed policy changes still receive an audit entry if the client disconnects after commit
- Read-only status, flow, and rule queries no longer grow the policy-change audit log
- Cancelled QoS operations terminate their PowerShell process tree before rollback begins
- Release driver device access is restricted to LocalSystem
- IPC rejects unknown JSON fields, unsupported protocol versions, oversized frames,
  invalid rule payloads, and more than 16 concurrent client handlers
- Rule validation rejects control characters in display names before terminal or UI output
- CLI and service startup reject unknown, duplicate, malformed, and control-character options
- CLI argument errors consistently use the documented usage exit code 2
- Service apply enforces the same 100-rule ceiling as rule-set import
- Service rejects aggregate rule sets that cannot fit the bounded interchange and IPC envelope
- Startup restore skips null or unnormalizable stored entries with an explicit warning
- Normal application packages exclude the driver, INF, catalog, test certificates,
  and native harness
- WDK builds preserve the reviewed `0.4.0.0` INF driver version instead of
  replacing it with a build-time version
- Expired rules are skipped during startup restoration and removed by the expiration worker

### Known limitations

- Download shaping and kernel packet queues are not implemented
- WFP driver load, unload, sleep, resume, HVCI, and Driver Verifier checks still
  require the documented disposable-VM test pass
- Public driver binaries require Microsoft signing
