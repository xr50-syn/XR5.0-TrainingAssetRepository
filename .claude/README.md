# Claude Code adapter

[AGENTS.md](../AGENTS.md) is the shared project entrypoint. Procedures live in
`docs/guides/`; no project knowledge is maintained only in this directory.

Both `.claude/skills/` and `.agents/skills/` package the same six workflows:
`e2e-verify`, `e2e-probe`, `api-probe`, `ai-assistant-probe`, `material-type`, and
`run-tests`. Each has a `<name>/SKILL.md` entrypoint with matching instructions.
The old flat `material-type.md` and `run-tests.md` paths are compatibility links.

See [Agent Skill Portability](../docs/design/agent-skill-portability.md) for the
shared workflow map and maintenance rules. Personal overrides belong in the
ignored `settings.local.json`, not in the tracked adapter or team settings.
