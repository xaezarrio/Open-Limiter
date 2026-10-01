# Contributor guidance

- Keep Windows-specific enforcement out of `OpenLimiter.Core`.
- A control that cannot perform its advertised action must be disabled with an explanation.
- Never claim that download limiting works until the WFP callout driver is built, signed, installed, and exercised on Windows 10 and 11.
- Security-sensitive changes need tests for rule ownership, argument handling, and fail-closed behavior.

<!-- antislop:start -->
## antislop
For UI, copy, people, mobile layout, or code comments work, read `antislop.md` (core) and then the skill for the task:
- UI / visual: `skills/antislop-ui/SKILL.md`
- Copy & text: `skills/antislop-copywriting/SKILL.md`
- People: `skills/antislop-human/SKILL.md`
- Mobile / responsive: `skills/antislop-layoutmobile/SKILL.md`
- Code comments: `skills/antislop-code/SKILL.md`
Before starting, ask the user when antislop applies: during the work, or after it is done.
<!-- antislop:end -->

