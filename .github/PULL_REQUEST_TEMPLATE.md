## What changed

Explain the user-visible outcome and the enforcement boundary affected.

## Verification

- [ ] `scripts/verify.ps1` passes
- [ ] New behavior has focused tests
- [ ] GUI changes include keyboard, focus, loading, empty, error, and compact-window checks
- [ ] Driver changes were loaded only in a recoverable test VM
- [ ] Release packages contain no driver, catalog, test certificate, or native harness
- [ ] Documentation and `CHANGELOG.md` are updated when behavior changed

## Safety and compatibility

Describe rollback, fail-open behavior, Windows version assumptions, and any manual VM checks. Contributions are licensed under GPL-3.0-only.
