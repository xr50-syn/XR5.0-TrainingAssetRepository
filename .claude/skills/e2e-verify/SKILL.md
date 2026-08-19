---
name: e2e-verify
description: Run the project's verification ladder (build, hermetic tests, functional e2e suite, optional authorization sweep) against the XR5.0 Training Asset Repository, bringing up the docker sandbox stack if needed. Use when asked to verify a change, run the tests, check nothing broke, do e2e or regression testing, or confirm the build is healthy before a merge.
---

# Verification ladder

Authoritative procedure: `docs/guides/verification-workflow.md`. Read it before deviating from
the steps below. The rules it encodes exist because each one has produced a wrong conclusion at
least once.

## Run it

```bash
./scripts/verify-e2e.sh                  # build + hermetic, functional if a stack is up
./scripts/verify-e2e.sh --up             # start the sandbox stack first, then run everything
./scripts/verify-e2e.sh --rung hermetic  # a single rung
./scripts/verify-e2e.sh --suite tenant   # functional rung, one suite
./scripts/verify-e2e.sh --with-authz     # add the authorization matrix sweep
```

Match the depth to the change: build only for docs and comments; build plus hermetic for
service or controller logic; the whole ladder for anything touching persistence, storage, auth
or tenant provisioning. The hermetic suite stubs the database, so it cannot catch a change in
how an identifier reaches MySQL.

## Non-negotiables

- **Never report a rung as passing when it did not run.** No stack means the functional rung is
  SKIPPED. Say so explicitly. The script already distinguishes these; preserve the distinction
  in your summary.
- **Rebuild the image after changing application code.** `--up` passes `--build`. A stale
  container silently tests the previous binary and makes a real change look like a no-op.
- **Do not disable the anonymous-bypass gate to make a run finish.** If `GET /api/auth/me`
  returns anything but `401` unauthenticated, authorization is off and a green run proves
  nothing. Fix the configuration instead.
- **Leave no test data behind.** Confirm the functional teardown line reports `0 failed`, and
  check for leftover `test_*` tenants if it does not.

## Reporting

Give the counts per rung, name anything skipped and why, and separate findings your change
caused from pre-existing issues you happened to notice. If a rung fails, stop and diagnose it
rather than running the remaining rungs to collect more red.

For verifying behavior that no existing suite covers, use the `e2e-probe` skill.
