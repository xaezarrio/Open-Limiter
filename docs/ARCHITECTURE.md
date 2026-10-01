# Architecture

OpenLimiter separates presentation, privileged policy management, and packet
enforcement so the GUI does not become a permanent administrator process.

## Current MVP

- `OpenLimiter.Core` owns rule models, validation, persistence, and stable names.
- `OpenLimiter.Windows` enumerates processes, creates application-scoped Windows
  Firewall rules, applies upload throttling through Policy-based QoS, and reads
  PID-owned TCP and UDP endpoint tables from the Windows IP Helper API.
- `OpenLimiter.Protocol` defines a versioned, length-prefixed JSON protocol with a
  64 KiB message ceiling and no arbitrary command operation. Protocol version 4
  carries structured driver snapshots and bounded rule-set replacement requests.
- `OpenLimiter.Service` authenticates the installed user through the named-pipe ACL,
  validates every request, owns the machine-wide rule store, and records audit JSONL.
- `driver/OpenLimiter.Wfp` is an unsigned pass-through kernel milestone. It registers
  two runtime callout keys, always continues classification, and exposes versioned
  capability and flow-event IOCTLs. Release builds restrict the control device to
  LocalSystem. It does not add persistent WFP filters or shape traffic.
- `driver/OpenLimiter.WfpHarness` owns temporary IPv4 and IPv6 flow-established
  callouts and inspection filters in a dynamic WFP session for isolated VM tests.
  Closing the harness removes those objects automatically.
- The classify callback records only sequence, PID, IP version, IP protocol,
  direction, and a bounded hash of the WFP application identity. A fixed 64-event
  ring avoids kernel heap growth and does not retain raw executable paths.
- User mode resolves the same WFP application identity for executable paths and
  applies the identical bounded FNV-1a hash. This lets the GUI name an event after
  its originating PID exits without sending raw paths through the driver IOCTL.
- The service compares that identity hash with saved rule executable hashes and
  annotates unambiguous flow events with the matching rule ID and display name.
- The policy service is the sole user-mode client of that IOCTL. Its ping response
  exposes version and counters, while a separate request returns the bounded flow
  snapshot to GUI and CLI clients without granting them a kernel device handle.
- When the driver is available, the policy service opens its own dynamic WFP session
  and atomically adds the provider, sublayer, callouts, and inspection filters. The
  Base Filtering Engine removes all session objects if the service exits or crashes.
  If the driver is unavailable at service startup, the service retries activation
  without blocking firewall or QoS requests. Driver availability and policy-session
  activation remain separate reported states.
- The WPF and CLI processes remain non-elevated and fail closed when the policy
  service is unavailable.
- Firewall rules are individually named and removed only by their owning rule ID.
- PowerShell receives an encoded script and every dynamic string is quoted before
  execution. No user value is appended as a raw command-line fragment.

The connection inventory is a point-in-time OS table, not a throughput estimate.
TCP rows include local and remote endpoints plus state. UDP rows represent bound
local endpoints and are not presented as established remote connections.

Policy-based QoS controls outbound traffic. It does not provide a real per-process
download limiter. The application reports that limitation instead of presenting
download limiting as successful.

## Process model

The complete product needs these processes:

1. A non-elevated WPF GUI for process selection, live traffic, and rule editing.
2. A Windows service that validates every request and owns persistent rules. The
   service and its MSI-managed install, start, stop, upgrade, and uninstall lifecycle
   are implemented; production binary signing remains.
3. A WFP callout driver for bidirectional byte accounting, queuing, and token-bucket
   shaping at flow or transport layers.

Application-aware blocking belongs at WFP ALE authorization layers. Traffic shaping
needs a callout because simple filters permit or block; they do not queue packets to
enforce a sustained byte rate.

## Trust boundary

The installer writes one authorized Windows SID. The pipe ACL grants that SID,
LocalSystem, and administrators access while explicitly denying network identities.
The service rejects unknown protocol fields and versions, oversized messages,
nonexistent executables, unsupported direction flags, and unbounded rates. Each IPC
client has a 15-second deadline, and the server caps concurrent handlers at 16. The
GUI is a client and is not trusted merely because it ships in the same installer.

Apply and remove operations serialize through the service. Enforcement happens
before persistence; a failed save triggers rollback to the previous Windows policy.
Rule-set import removes and applies policies under the same mutation lock, with
compensating rollback to the previous set on enforcement or persistence failure.
Pause is an apply operation with `Enabled = false`; it removes current enforcement
while retaining the complete saved rule. Resume applies the same rule with
`Enabled = true`. Saved enabled rules are restored when the service starts, while
paused rules remain stored and unenforced.

## References

- [GetExtendedTcpTable](https://learn.microsoft.com/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable)
- [MIB_TCPROW_OWNER_PID](https://learn.microsoft.com/windows/win32/api/tcpmib/ns-tcpmib-mib_tcprow_owner_pid)
- [MIB_UDPTABLE_OWNER_PID](https://learn.microsoft.com/windows/win32/api/udpmib/ns-udpmib-mib_udptable_owner_pid)
