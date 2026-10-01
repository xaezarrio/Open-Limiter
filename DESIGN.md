# OpenLimiter design direction

- Identity: an industrial traffic console for Windows, built around decisive per-process control rather than decorative dashboards.
- Audience: power users, developers, gamers, and support technicians who need to identify a process and change its network policy quickly.
- Personality: focused, mechanical, transparent, and calm under load.
- Palette: graphite and steel surfaces, warm amber reserved for policy enforcement, and cool cyan reserved for inbound traffic identity.
- Typography: Segoe UI for native Windows clarity at dense process-list sizes. Numeric values use tabular figures rather than a decorative monospace face.
- Layout: a dominant live process rail feeds a wide rule inspector. Saved policies stay visible below the inspector so the user can verify what persists.
- Motif: paired inbound and outbound lanes use directional labels and restrained color, echoing a physical network gate without imitating NetLimiter.
- Theme: dark by default because this is a long-running monitoring tool commonly kept beside games, terminals, and diagnostic utilities.
- Motion: state transitions only. No decorative or looping animation.
- Dial: ENERGY 2 / RHYTHM 2 / MOTION 1.

## Decision reasons

- Graphite reduces glare during long monitoring sessions and gives process names the strongest contrast.
- Amber appears only where a policy will change system behavior, so it reads as deliberate action rather than decoration.
- Cyan identifies inbound traffic and never substitutes for text labels.
- The process rail is the focal point because selecting the correct executable is the highest-risk user decision.
- Process rows are grouped by executable path so multi-process applications produce one enforceable choice without merging unrelated binaries.
- The asymmetric split gives process discovery more scanning room while preserving a stable rule editor.
- Cards are limited to traffic lanes and saved-policy rows because those items have independent state and actions.
- No traffic chart appears until the product can supply measured traffic instead of placeholder numbers.
- The header reports the real policy-service connection because every privileged action depends on that boundary.
- Destructive rule actions stay inside Saved policies and require confirmation so users can identify exactly which persistent enforcement they are removing.
- Pause and Resume share each saved-policy row because they change that rule's real enforcement state while preserving its configuration; the adjacent text label prevents color-only status.
