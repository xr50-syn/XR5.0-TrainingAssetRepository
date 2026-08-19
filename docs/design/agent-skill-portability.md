# Agent Skill Portability

**Status:** Adopted

How this repository offers its verification workflow to coding agents without requiring any
particular one. Written when the Claude Code adapter under `.claude/` was added, to keep that
from becoming the privileged path.

## The problem

Agent vendors discover packaged instructions from different locations — Claude Code uses
`.claude/skills/`, while Codex discovers repository skills from `.agents/skills/`. Others have
rules files or no adapter mechanism at all. Encoding the verification workflow in one vendor's
location makes contributors using a different tool second-class, and makes the workflow itself
invisible to anyone reading the repository without an agent.

[AGENTS.md](../../AGENTS.md) already commits us against this: *"Do not require a particular
model, agent vendor, IDE, or proprietary tool"* and *"Vendor adapters ... are optional local
configuration and should point back to this file."*

## Principle: neutral core, thin adapters

```
docs/guides/verification-workflow.md   <- the procedure, in prose
scripts/verify-e2e.sh                  <- the procedure, executable
AGENTS.md                              <- the rules, and pointers to both
        |
        +-- .claude/skills/*/SKILL.md        thin adapter: triggers + checklist
        +-- .agents/skills/*/SKILL.md        thin Codex adapter: triggers + checklist
        +-- (no adapter)                     still works: AGENTS.md points at the guide
```

An adapter may contain trigger conditions, a short checklist, and a pointer. It may **not**
contain project knowledge that exists nowhere else. The test: delete every adapter directory
and the repository must lose no information.

The bottom row matters most. An agent with no adapter at all should still reach the right
procedure, because `AGENTS.md` is read natively by most tools and names the guide explicitly.
Adapters are an ergonomic improvement, never the mechanism.

## Adapter contract

Any adapter, for any vendor, must:

1. **Point to `docs/guides/verification-workflow.md`** as authoritative, and say that the guide
   wins on any disagreement.
2. **Invoke `scripts/verify-e2e.sh`** rather than restating its commands, so flags and rungs
   cannot drift.
3. **Carry the honesty rules verbatim in substance**: a skipped rung is reported as skipped;
   the anonymous-bypass gate is never disabled to make a run finish; the image is rebuilt after
   application code changes; probes clean up and verify the cleanup.
4. **Add no project facts of its own.** New knowledge goes in the guide or AGENTS.md, and the
   adapter references it.
5. **Keep personal configuration untracked** — `settings.local.json` and equivalents stay
   gitignored.

## Codex adapter

Codex reads `AGENTS.md` natively and supports repository-scoped agent skills. That makes it a
good second adapter and proof that the neutral core is genuinely neutral.

### Mechanism confirmed

The original proposal assumed project prompt files under `.codex/prompts/`. That assumption was
checked before implementation and was no longer current. Codex CLI 0.148.0, the installed
release when this adapter was adopted, follows the agent skills mechanism documented in the
[official Codex skill documentation](https://learn.chatgpt.com/codex/build-skills):

- repository skills live under `.agents/skills/<name>/SKILL.md`;
- Codex scans from the current directory up to the repository root without a project config
  entry;
- users can invoke a skill explicitly as `$e2e-verify` or `$e2e-probe`;
- Codex can select either skill implicitly from its frontmatter description.

The packaging changed; the vendor-neutral workflow did not.

### Zero-adapter path

The zero-adapter path was verified before adding `.agents/skills/`: Codex read `AGENTS.md`,
followed its pointer to the verification guide, and ran `scripts/verify-e2e.sh`. The adapter is
therefore a convenience and stays minimal.

If that path ever stops working, make the pointer in `AGENTS.md` more prominent rather than
compensating with project knowledge inside a Codex-specific file.

### Files

The Codex adapter mirrors the two Claude Code skills:

| File | Mirrors | Content |
|---|---|---|
| `.agents/skills/e2e-verify/SKILL.md` | `.claude/skills/e2e-verify/SKILL.md` | ladder invocation + non-negotiables |
| `.agents/skills/e2e-probe/SKILL.md` | `.claude/skills/e2e-probe/SKILL.md` | probe procedure + assertion checklist |

The adapters use Codex's required `name` and `description` frontmatter. They need no scripts,
assets, generated metadata or repository-local configuration.

## Other agents

Cursor (`.cursor/rules/`), Aider (`CONVENTIONS.md`), Copilot
(`.github/copilot-instructions.md`) and Continue (`.continuerules`) all take a single
instructions file. For each, the entire adapter is a few lines pointing at `AGENTS.md` and the
verification guide. Add one when a contributor actually uses that tool — speculative adapters
rot silently because nobody runs them.

## Keeping adapters from drifting

Drift is the real risk: adapters are duplicated prose, and duplicated prose diverges.

- **The guide is the only place a procedure changes.** Adapters change only when the *set* of
  skills changes.
- **Adapters must not restate command syntax.** They name `scripts/verify-e2e.sh`; the script's
  own `--help` is the interface.
- **When behavior changes, update the guide and AGENTS.md together** — already an AGENTS.md
  rule, extended here to cover adapters.
- **Review rule:** a pull request touching `.claude/` or `.agents/skills/` without touching
  `docs/guides/` is either adapter packaging or trigger wording only, or it is a mistake. Ask
  which.

## What this deliberately does not do

- **No CI enforcement of adapter parity.** Two adapters do not justify a checker; revisit at
  four.
- **No generated adapters.** A generator is more machinery than the few files it would emit.
- **No vendor-specific behavior.** If an agent needs different *instructions* rather than a
  different *file format*, that is a signal the guide is underspecified — fix the guide.
