# Claude Code adapter

Optional local tooling. Everything authoritative lives in vendor-neutral form:

- [AGENTS.md](../AGENTS.md) — architecture constraints, coding conventions, verification rules
- [docs/guides/verification-workflow.md](../docs/guides/verification-workflow.md) — the
  verification ladder and how to write a targeted probe
- `scripts/verify-e2e.sh` — the executable ladder

The skills here are thin pointers to those documents. They add trigger conditions and a short
checklist; they do not add project knowledge of their own. If a skill and AGENTS.md ever
disagree, AGENTS.md wins and the skill is the thing to fix.

| Skill | Use for |
|---|---|
| `e2e-verify` | Running the verification ladder against a change |
| `e2e-probe` | Proving a specific change works, where no suite covers it yet |

Contributors using other agents are not expected to install this. See
[docs/design/agent-skill-portability.md](../docs/design/agent-skill-portability.md) for how the
same capability is offered to other tools.

`settings.local.json` is gitignored; personal overrides belong there rather than in a tracked
file.
