# Verification Workflow

How to verify a change to this repository, from a one-line fix to a change that crosses the
database and storage boundaries. This guide is vendor-neutral: every step is an ordinary
command that a human or any coding agent can run. Agent adapters under `.claude/` and
`.codex/` are thin pointers to this document and add nothing of their own.

The short version of the rules lives in [AGENTS.md](../../AGENTS.md) under "Verification".

## The ladder

Run the cheapest check that can still fail, and stop at the first failing rung so you diagnose
one problem instead of a cascade.

| Rung | Command | Needs infrastructure | Catches |
|---|---|---|---|
| Build | `dotnet build` | no | compile errors, broken signatures after a refactor |
| Hermetic | `dotnet test tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj` | no | logic regressions, controller behavior, mapping rules |
| Functional | `cd tests/functional && npm test` | yes | real HTTP, auth, persistence, storage |
| Authorization | `./scripts/authz-probe.sh` | yes | role matrix drift across every tier |
| Targeted probe | ad-hoc, see below | usually | the specific behavior your change introduced |

`scripts/verify-e2e.sh` runs the ladder for you:

```bash
./scripts/verify-e2e.sh                  # build + hermetic, then functional if a stack is up
./scripts/verify-e2e.sh --up             # start the sandbox stack first, then run everything
./scripts/verify-e2e.sh --rung hermetic  # a single rung
./scripts/verify-e2e.sh --suite tenant   # functional rung, one suite
./scripts/verify-e2e.sh --with-authz     # add the authorization matrix sweep
./scripts/verify-e2e.sh --down           # stop the stack (volumes preserved)
```

It never drops volumes and never deletes tenants.

## Choosing rungs

- **Comment, doc, or log-message change** — build only.
- **Logic inside a service or controller** — build and hermetic.
- **Anything touching persistence, storage, auth, or tenant provisioning** — the whole ladder,
  plus a targeted probe. The hermetic suite stubs the database, so a change to how an
  identifier reaches MySQL cannot fail there.
- **A refactor that moves code without changing behavior** — build and hermetic, then the
  functional suite as a regression net.

## Bringing up a stack

```bash
cp .env.sandbox.example .env     # then replace every change_me value
./scripts/verify-e2e.sh --up
```

The sandbox profile starts MariaDB, MinIO, Keycloak and the API. Check
`http://localhost:5286/health` before believing any functional result. If the endpoint is not
reachable, **report the functional rung as skipped**. A skipped check is a normal outcome; a
check reported as passing when it never ran is a defect in the report.

## Two false greens to guard against

**The anonymous bypass.** With `IAM__AllowAnonymousInDevelopment=true` every authorization
policy succeeds, so the functional suite goes green while proving nothing about auth. Both
`scripts/verify-e2e.sh` and `scripts/authz-probe.sh` gate on `GET /api/auth/me` returning
`401` to an unauthenticated caller and refuse to run otherwise. Do not remove that gate to
make a run finish.

**A stale container.** `docker compose up -d` will happily leave the previous image running.
After changing application code, rebuild — `./scripts/verify-e2e.sh --up` passes `--build` —
or you will be testing the old binary and concluding that your change had no effect.

## Writing a targeted probe

The suites cover what the repository already does. A change introduces behavior nothing
covers yet, so it needs its own probe. Probes are throwaway scripts run against a live stack;
keep them outside the repository (a scratch directory) unless you are promoting one to a suite.

Five rules make a probe trustworthy.

**1. Derive the cases from the diff, not from the commit message.** Read what the code now
accepts, rejects, and derives. A probe written from the description tests the intent; a probe
written from the diff tests the implementation, and the gap between them is where bugs live.

**2. Include a control.** Always probe one case that must behave the *opposite* way, and one
unchanged case that must behave the *same* way as before. When an assertion fails, the control
tells you whether you found a bug or wrote a bad assertion.

> This is not hypothetical. A probe for the tenant-naming change asserted that the diagnose
> endpoint reports the derived schema name. It failed — but it failed identically for the
> control tenant, which immediately identified the assertion as wrong (that response carries
> no schema field) rather than the code. Without the control, that would have been filed as a
> bug.

**3. Assert below the API when the change reaches below the API.** A `200` proves the request
was accepted, not that the right thing was persisted. For a change to tenant provisioning,
check the schema list directly:

```bash
docker exec mariadb mysql -uroot -p"$XR50_REPO_DB_PASSWORD" -N -B \
  -e "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME LIKE 'xr50_tenant_%'"
```

Useful assertions at that layer: the derived name is what you expected; no second schema
appeared; a rejected request left nothing behind; a deletion actually dropped the schema; two
tenants provisioned through different paths have the same table count.

**4. Clean up, then verify the cleanup.** Record a baseline before the probe and diff against
it at the end. Deleting what you think you created is not the same as confirming nothing is
left — a partially-provisioned resource is exactly the kind of thing a naming bug produces.

```
baseline  -> run probe -> delete created resources -> assert (final == baseline)
```

**5. Report what ran.** Give counts, name what was skipped and why, and state which findings
are pre-existing rather than caused by the change under test.

## Interpreting failures

- **Failed for the control too** — your assertion is wrong, not the code.
- **Passes hermetically, fails functionally** — the difference is real infrastructure: a
  stubbed database hides identifier handling, collation, and connection-string derivation.
- **Fails only on a second run** — the first run leaked state. Check teardown before
  suspecting the change.
- **`MSB3030` during `dotnet test`** — nested build artifacts under
  `tests/XR50TrainingAssetRepo.Tests/bin`. Delete `bin` and `obj` there; both are gitignored.
  This is an environment problem, never a reason to edit source.

## Promoting a probe

Move a probe into a permanent suite when the behavior it covers is a contract others will rely
on rather than a one-off question:

- Pure mapping or validation logic with no infrastructure needed → a hermetic xUnit test in
  `tests/XR50TrainingAssetRepo.Tests/`. Prefer this; it runs in seconds and never flakes.
- Behavior that genuinely requires HTTP, auth, or storage → a Jest suite in
  `tests/functional/suites/`, wired into `package.json` and the routing table in AGENTS.md.

If the probe needed a running database only to observe a side effect, the logic underneath it
can usually be tested hermetically instead. The tenant-naming change ended up with both: unit
tests for the name-to-identifier mapping, and a live probe for the provisioning path that
mapping feeds.
