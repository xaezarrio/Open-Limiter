# Rule-set interchange format

OpenLimiter CLI exports UTF-8 JSON with a required schema version. Schema version
1 is bounded to 100 rules and 60 KiB so the complete document fits inside one
policy-service request after protocol framing.

```json
{
  "schemaVersion": 1,
  "exportedAtUtc": "2026-09-26T02:00:00+00:00",
  "rules": [
    {
      "id": "a4a1884d-848a-47fa-bf77-2637f596b229",
      "displayName": "Example browser",
      "executablePath": "C:\\Program Files\\Example\\browser.exe",
      "enabled": true,
      "blockedDirections": "Both",
      "uploadLimitBitsPerSecond": null,
      "downloadLimitBitsPerSecond": null
    }
  ]
}
```

`blockedDirections` is one of `None`, `Inbound`, `Outbound`, or `Both`. Rates are
integer bits per second. Unknown fields, numeric enum values, unsupported schema
versions, null rules, duplicate IDs, duplicate executable paths, missing
executables, conflicting controls, and unbounded rates are rejected.

`enabled: true` means the service should enforce the saved rule. `enabled: false`
is a paused rule: its block and upload-limit configuration remains in the document,
but the service removes its Windows enforcement and does not restore it at startup.

Import is a replacement operation, not a merge. The CLI requires
`--confirm replace-all`; the service validates every rule before touching Windows
policy. It then removes the previous set, applies the imported set, and persists it
under one mutation lock. Enforcement failure, persistence failure, or request
cancellation triggers compensating rollback to the previous set.

Export files contain executable paths and custom display names. Treat them as
potentially sensitive configuration. Do not use the service's internal
`%ProgramData%\OpenLimiter\rules.json` as an interchange file.
