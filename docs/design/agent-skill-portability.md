# Agent Skill Portability

**Status:** Proposed

How this repository offers its verification workflow to coding agents without requiring any
particular one. Written when the Claude Code adapter under `.claude/` was added, to keep that
from becoming the privileged path.

## The problem

Agent vendors each have their own format for packaged instructions — Claude Code has skills,
Codex has prompt files, others have rules files or none at all. Encoding the verification
workflow in one vendor's format makes contributors using a different tool second-class, and
makes the workflow itself invisible to anyone reading the repository without an agent.

[AGENTS.md](../../AGENTS.md) already commits us against this: *"Do not require a particular
model, agent vendor, IDE, or proprietary tool"* and *"Vendor adapters ... are optional local
configuration and should point back to this file."*

## Principle: neutral core, thin adapters

```
docs/guides/verification-workflow.md   <- the procedure, in prose
scripts/verify-e2e.sh                  <- the procedure, executable
AGENTS.md                              <- the rules, and pointers to both
        |
        +-- .claude/skills/*/SKILL.md   thin adapter: triggers + checklist
        +-- .codex/prompts/*.md         thin adapter: triggers + checklist
        +-- (no adapter)                still works: AGENTS.md points at the guide
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

## Plan for Codex

Codex is open source, reads `AGENTS.md` natively, and supports user-defined prompt files. That
makes it the cheapest second adapter and a good proof that the neutral core is genuinely
neutral.

### Step 0 — confirm the mechanism (do this first)

The plan below assumes Codex discovers Markdown prompt files from a project directory and
exposes them as invocable commands. **Confirm against the installed Codex release before
building anything**, because this is the part most likely to have changed:

```bash
codex --version
codex --help                 # look for prompt/command discovery flags
ls ~/.codex/                 # config.toml, prompts/, AGENTS.md
```

Specifically establish: (a) whether prompts can live in the project tree or only in `~/.codex/`,
(b) whether discovery is automatic or needs a config entry, (c) whether prompts are
user-invoked only or can be model-selected from a description. The answers change only the
*packaging* below, never the content.

### Step 1 — verify the zero-adapter path already works

Before adding anything, check that Codex reaches the workflow through `AGENTS.md` alone. Open
Codex in a clean checkout and ask it to verify a trivial change. It should find the
verification section, follow the pointer to the guide, and run `scripts/verify-e2e.sh`.

If that works, the adapter is a convenience and can stay minimal. If it does not, the fix is to
make the pointer in `AGENTS.md` more prominent — a fix that benefits every agent — rather than
to compensate inside a Codex-specific file.

### Step 2 — add `.codex/prompts/`

Mirror the two Claude skills as prompt files:

| File | Mirrors | Content |
|---|---|---|
| `.codex/prompts/e2e-verify.md` | `.claude/skills/e2e-verify/SKILL.md` | ladder invocation + non-negotiables |
| `.codex/prompts/e2e-probe.md` | `.claude/skills/e2e-probe/SKILL.md` | probe procedure + assertion checklist |
| `.codex/README.md` | `.claude/README.md` | adapter design, pointer back to AGENTS.md |

Adapt only the frontmatter and invocation convention. If Codex prompts are user-invoked rather
than model-selected, fold the Claude `description:` trigger conditions into a "Use this when"
line in the body so the information is not lost.

### Step 3 — gitignore

Already prepared: `.codex/settings.local.json` is ignored while the rest of `.codex/` tracks.

### Step 4 — declare it

Add a row to the adapter table in `AGENTS.md` and this document's status becomes Adopted.

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
- **Review rule:** a pull request touching `.claude/` or `.codex/` without touching
  `docs/guides/` is either a trigger-wording fix or a mistake. Ask which.

## What this deliberately does not do

- **No CI enforcement of adapter parity.** Two adapters do not justify a checker; revisit at
  four.
- **No generated adapters.** A generator is more machinery than the few files it would emit.
- **No vendor-specific behavior.** If an agent needs different *instructions* rather than a
  different *file format*, that is a signal the guide is underspecified — fix the guide.
