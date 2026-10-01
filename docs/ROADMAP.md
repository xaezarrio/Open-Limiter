# Roadmap

## Phase 1: safe rule control

- Process discovery with explicit handling for protected processes
- Inbound and outbound application blocking through Windows Firewall
- Per-application upload limiting through Policy-based QoS
- Durable rules and cleanup of product-owned policies
- Administrator status and actionable error states

## Phase 2: privileged agent

- Implemented: Windows service with SID-restricted named-pipe IPC
- Implemented: transactional apply, remove, clear, and rollback
- Implemented: rules restored after service or machine restart
- Implemented: JSONL audit log for policy changes
- Implemented: self-contained x64 MSI integration with service install and uninstall cleanup
- Remaining: production Authenticode code signing

## Phase 3: observability and community release

- Implemented: honest GUI and CLI driver availability and flow identity states
- Implemented: grouped and detailed CLI TCP connection and bound UDP endpoint views backed by Windows IP Helper data
- Implemented: per-process throughput sampling backed by measured ETW byte counters
- Implemented: recurring profile schedules and temporary rule overrides
- Implemented: system tray with quick global and per-rule Pause and Resume
- Implemented: reusable named policy profiles with transactional activation
- Remaining: rule priorities and network zones
- Implemented: CLI import/export with schema versioning and rule-set rollback
- Implemented: persistent Pause and Resume overrides in the GUI and CLI
- Reproducible packaging, contribution guide, security policy, and release checks

## Backlog: download limiting and bidirectional shaping

Download limiting is deferred and is not part of the active release plan. The
existing WFP research foundation remains in the repository for future work:

- Implemented: buildable x64 pass-through WFP callout source with versioned capability IOCTL
- Implemented: dynamic-session user-mode harness for IPv4 and IPv6 flow-established filters
- Implemented: service-owned dynamic filter lifecycle with atomic registration and cleanup
- Implemented: fixed-size kernel flow ring with identity, direction, protocol, IP version, and endpoints
- Implemented: application identity to saved-rule association in service telemetry
- Backlog: TCP, UDP, and QUIC byte accounting with explicit flow cleanup
- Backlog: token-bucket queues for upload and download
- Backlog: Driver Verifier, HVCI, sleep/resume, upgrade, and uninstall testing
- Backlog: Microsoft driver signing workflow for public binaries
