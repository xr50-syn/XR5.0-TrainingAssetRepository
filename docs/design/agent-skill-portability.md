# Agent Skill Portability

**Status:** Adopted

## Neutral core, thin adapters

[AGENTS.md](../../AGENTS.md) owns shared project rules and links every workflow.
Procedures live in ordinary Markdown under `docs/guides/`; executable helpers
live under `scripts/`. An agent or contributor needs no vendor adapter to use them.

The repository tracks matching `.claude/skills/<name>/SKILL.md` and
`.agents/skills/<name>/SKILL.md` entrypoints. Each carries a name, trigger description,
and pointers to the shared procedure, not its own copy of project instructions.
The Claude entrypoint imports AGENTS.md. Removing the adapter directories must
not remove project knowledge.

## Workflow map

| Skill in both directories | Shared procedure |
|---|---|
| `e2e-verify` | [Verification Workflow](../guides/verification-workflow.md), using `scripts/verify-e2e.sh` |
| `e2e-probe` | [Verification Workflow: targeted probes](../guides/verification-workflow.md#writing-a-targeted-probe) |
| `api-probe` | [API Probe](../guides/api-probe.md), the focused HTTP variant of a targeted probe |
| `ai-assistant-probe` | [AI Assistant / DataLens Probe](../guides/ai-assistant-probe.md) |
| `material-type` | [Material Type Changes](../guides/material-type.md) |
| `run-tests` | [Testing](../guides/testing.md), with the verification guide for rung selection |

The old `.claude/skills/material-type.md` and `run-tests.md` files only redirect
existing links. Discoverable skills use the directory-based layout above.

## Maintenance contract

- Change rules in AGENTS.md and procedures in the relevant shared guide. Adapters
  change when task routing, discovery descriptions, or packaging changes.
- Keep the skill names, descriptions, and pointers paired across both directories.
  Resolve links relative to each Markdown file; run commands from the repository root.
- Shared guides win over adapters on any disagreement. Repair the adapter instead
  of introducing vendor-specific project behavior.
- Keep team adapters tracked and personal settings, plans, and session state ignored.
  Do not change tool permissions merely to make a skill runnable.
- A workflow does not grant authority to mutate live systems. Diagnosis stays
  read-only unless the requested task authorizes a live probe or implementation.
- Review new workflows for the zero-adapter path: each must be reachable from
  AGENTS.md, with no required reference back into a vendor directory.

Before merging packaging changes, compare the skill-name sets and paired files,
check frontmatter and local links, and verify Git does not ignore the adapters.
No generator or tool-specific execution dependency is required.
