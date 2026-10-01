# Security policy

OpenLimiter changes host firewall and traffic policy, so security reports should not
be posted in a public issue.

After the GitHub repository is created, use its private security-advisory form to
report vulnerabilities. Include the affected version, Windows version, reproduction
steps, required privileges, and whether the issue can leave stale policies behind.

Pre-release builds are not yet supported for production use. The project currently
has an unsigned service and an unsigned development-only WFP driver, with no
security-support SLA. The normal release package excludes the driver, test
certificates, and native harness.

The service installer copies its binaries into `%ProgramFiles%\OpenLimiter\Service`
and restricts modification to LocalSystem and administrators before registering the
LocalSystem service. Do not register a service executable from a user-writable build
or download directory.

Rule-set exports contain executable paths and custom labels. Store them as private
configuration when paths reveal usernames or installed software. Treat imported
JSON as untrusted: review it before using `--confirm replace-all`. The service still
applies strict schema, path, duplication, direction, and rate validation before it
changes Windows policy.
