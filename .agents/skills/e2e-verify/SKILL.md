---
name: e2e-verify
description: Verify changes to the XR5.0 Training Asset Repository with its build, hermetic, functional, and optional authorization checks. Use when asked to run tests, check a change, perform end-to-end or regression verification, or confirm the repository is healthy before a merge.
---

# Verify a repository change

Read `docs/guides/verification-workflow.md`; it is authoritative and wins if this adapter
disagrees with it. Follow the verification section in `AGENTS.md` for change-specific routing.

Use `./scripts/verify-e2e.sh` as the executable interface. Read its `--help`, choose the
smallest adequate rungs for the change, and stop to diagnose the first failure.

Preserve these reporting and safety invariants:

- Report every rung with its counts. Report an unavailable functional or authorization rung as
  skipped, never passed.
- Keep the anonymous-bypass gate enabled; do not weaken authentication to obtain a green run.
- Rebuild the application image before live verification of application-code changes.
- Confirm functional teardown reports no cleanup failures. If it does not, investigate and
  report possible leftover test resources.

If existing suites do not cover the changed behavior, use `$e2e-probe` after the required
ladder is green.
