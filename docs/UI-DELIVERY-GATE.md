# UI delivery gate

Status: PASS for the current MVP scope. Firewall-changing actions were not invoked
on the developer machine. Their handlers, IPC validation, transactional persistence,
and enforcement orchestration are covered by build checks, fake backends, and a real
local named-pipe integration test.

## Hard gate

- R-02 PASS: application copy contains no em dash characters.
- R-03 PASS: the 1000 x 650 capture has no horizontal overflow; the inspector becomes vertically scrollable.
- R-17 PASS: executable-group and instance counts are calculated from the current process snapshot; policy counts come from saved rules.
- R-18 PASS: the product has no testimonials or fabricated people.
- R-23 PASS: no logo, avatar, illustration, or other assumed asset was created; the product name is rendered as text.
- R-24 PASS: the desktop application has no navigation links.
- R-25 PASS: primary text is 17.30:1, secondary text is 8.15:1, action text is 10.59:1, traffic labels are 8.66:1, errors are 7.14:1, and control boundaries are at least 3.21:1.
- R-26 PASS: Refresh reloads process, policy, version, driver, traffic, profile, and schedule data; process selection opens the editor; search and traffic sorting filter or order the process rail; checkboxes and duration input change rule configuration; Apply, Pause, Resume, Remove, profile, and schedule actions use the policy service; Retry service reconnects after an unavailable state; Refresh flows queries live driver telemetry.
- R-27 PASS: process, policy, and driver views contain loading, empty, and actionable error or unavailable states.
- R-28 PASS: the product has no FAQ.
- R-32 PASS: native WPF controls retain keyboard behavior and each custom control template has a visible keyboard-focus state.
- R-33 PASS: UI changes were authored in XAML and C#; no source-rewriting patch script ships with the product.
- R-34 PASS: one deliberate dark theme ships; there is no incomplete theme toggle.
- R-35 PASS: Release build launched from source; matching-service, telemetry-unavailable, version-mismatch, driver-offline, automation-populated, and compact states were rendered at 1440 x 1000 and 1000 x 650; the version-mismatch state was also verified against an installed 0.4.3 service; all 142 automated tests pass; native Release driver and harness builds pass; the MSI was administratively extracted and its service, shortcut, cleanup, upgrade, and payload tables were inspected; external policy mutation is isolated behind tested service and enforcement boundaries.
- R-36 PASS: the UI makes no compliance, security-certification, performance, or adoption claims.
- R-37 PASS: `DESIGN.md` records audience, visual language, palette, typography, layout, motion, and dials.
- R-38 PASS: process and policy content is real; throughput comes from kernel ETW, driver events come from the kernel ring, and unavailable telemetry is labelled honestly; download limiting is visibly marked as a backlog item.

## Purpose gate

- R-01 PASS: cyan identifies inbound state and amber identifies policy-changing outbound actions; there are no gradients.
- R-04 PASS: no generic feature icons or decorative emoji are used.
- R-06 PASS: Segoe UI was selected for native Windows clarity in dense process lists.
- R-07 PASS: the background uses solid graphite surfaces with no stock grid or pattern.
- R-08 PASS: buttons contain no decorative arrows.
- R-09 PASS: the header label communicates the real policy-service connection and uses no glow or decorative status treatment.
- R-10 PASS: no glassmorphism is used.
- R-12 PASS: panels are separated by borders and surfaces rather than universal shadows.
- R-13 PASS: no glow is used.
- R-14 PASS: the two traffic lanes are cards because each owns independent direction, state, and enforcement controls.
- R-19 PASS: MOTION 1 is honored; no looping or decorative animation exists.
- R-22 PASS: no generic illustration is used.

## Liveliness

- Dials PASS: ENERGY 2, RHYTHM 2, and MOTION 1 are declared in `DESIGN.md`.
- Dial consistency PASS: hierarchy is moderately expressive, composition has one asymmetric split, and motion is limited to state changes.
- Focal point PASS: the process rail is the first and strongest decision surface.
- Whitespace PASS: spacing separates process discovery, rule editing, and persistent-policy verification.
- Accent PASS: cyan and amber are restricted to traffic identity, focus, counts, and enforcement actions.
- Identity motif PASS: paired inbound and outbound gates repeat throughout the editor and saved-policy language.
- Design Read PASS: the UI was declared as an industrial Windows traffic monitor for power users before generation.

## Craftsmanship and quality locks

- C-1 PASS: major visual decisions and their purposes are recorded in `DESIGN.md`.
- C-2 PASS: every enabled interactive control has an implemented handler.
- C-3 PASS: every visible section supports process selection, rule editing, saved-policy verification, or WFP flow inspection.
- C-4 PASS: loading, empty, error, disabled, active-rule, paused-rule, temporary-rule, service-connected, service-version-mismatch, service-unavailable, traffic-unavailable, driver-offline, profile, schedule, tray, default-size, and compact-size behavior are represented.
- C-5 PASS: all displayed operational data comes from process enumeration, saved rules, or the bounded WFP event buffer; same-path instances are grouped and ambiguous same-name executables remain separate.
- R-05 PASS: the application uses a task-specific process rail and gate inspector rather than a generic dashboard shell.
- R-11 PASS: radii vary from 4 to 7 pixels by control and surface hierarchy; no element is pill-shaped.
- R-15 PASS: actions are named Refresh, Refresh flows, Retry service, Apply network rule, Pause rule, Resume rule, and Remove rule.
- R-16 PASS: application copy contains no AI marketing buzzwords.
- R-20 PASS: paired traffic gates, process-first hierarchy, and explicit driver boundary give the interface a product-specific identity.
- R-21 PASS: dark mode is justified for a long-running network monitor commonly used beside games and diagnostic tools.
- R-29 PASS: graphite and steel form the core palette, with amber and cyan carrying narrowly defined roles.
- R-30 PASS: the layout does not reproduce NetLimiter or another named product.
- R-31 PASS: color, layout, typography, spacing, cards, assets, and motion each have a one-line reason in `DESIGN.md`.

## Human and UI checks

- Contrast PASS: all active text pairings meet 4.5:1 and interactive boundaries meet 3:1.
- Focus PASS: buttons, text boxes, checkboxes, and list rows expose visible focus treatment.
- Keyboard PASS: controls use native WPF tab order and activation behavior; no mouse-only menu or dialog exists.
- State communication PASS: color is paired with labels such as Inbound, Outbound, Active, Paused, Temporary, Policy service connected, Traffic unavailable, Service update required, WFP driver offline, and Policy service unavailable.
- Data states PASS: process, saved-policy, and driver telemetry regions have named loading, empty, unavailable, and error states with a next action.
- Compact layout PASS: the 1000 x 650 rendering avoids collision and uses vertical scrolling instead of clipping.
- Content honesty PASS: no fake throughput chart, connection count, or downloaded-rate success state is shown.
- Interaction completeness PASS: Pause removes active Firewall and QoS enforcement while retaining the saved configuration; Resume reapplies it; temporary rules expire service-side; profiles activate transactionally; schedules run at most once per local day with delayed-start catch-up; deferred download limiting is disabled, marked as backlog, and never presented as a working control.

## Release evidence

- Default unavailable state: `artifacts/ui/openlimiter-0.5.0-release.png`
- Connected automation state: `artifacts/ui/openlimiter-0.5.0-connected-final.png`
- Compact state: `artifacts/ui/openlimiter-0.5.0-compact.png`
- Installer: `artifacts/installer/OpenLimiter-0.5.0-win-x64.msi`
