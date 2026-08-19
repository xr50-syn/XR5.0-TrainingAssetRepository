---
name: e2e-probe
description: Design and run a targeted end-to-end probe for a specific change in the XR5.0 Training Asset Repository, against a live stack, with a control group, database-level assertions and verified cleanup. Use when a change introduces behavior the existing suites do not cover - tenant provisioning, storage keys, auth rules, migrations - or when asked to prove a specific fix works end to end.
---

# Targeted change probe

Authoritative procedure: `docs/guides/verification-workflow.md`, section "Writing a targeted
probe". Run the `e2e-verify` skill first — a probe is only meaningful on top of a green ladder.

The existing suites cover what the repository already does. A change introduces behavior
nothing covers yet, which is exactly the behavior most likely to be wrong.

## Procedure

1. **Read the diff, not the commit message.** List what the code now accepts, rejects, and
   derives. Probe the implementation; the gap between it and the stated intent is where bugs
   live.
2. **Enumerate cases from that list**, including boundaries: the empty and maximum-length
   input, each separator or special character the validator mentions, and both orderings of any
   collision or conflict rule.
3. **Add controls.** At least one case that must behave the opposite way, and one unchanged
   case that must behave exactly as before. When an assertion fails, the control tells you
   whether you found a bug or wrote a bad assertion — this has already saved one false bug
   report on this repository.
4. **Record a baseline** of the state you are about to disturb (tenant schema list, tenant
   registry, bucket contents).
5. **Write the probe as a throwaway script in a scratch directory, not in the repository.**
   `NODE_PATH=tests/functional/node_modules` makes `axios` available to a standalone script.
6. **Assert below the API.** A `200` proves acceptance, not correct persistence. Query MySQL
   directly for anything touching provisioning, migrations, or identifiers.
7. **Clean up, then diff against the baseline** and state the result. Deleting what you think
   you created is not the same as confirming nothing was left behind.

## Assertions worth making

- The derived database name is exactly what was expected, and no second schema appeared.
- A rejected request left nothing behind — no partially-provisioned database, no orphan row.
- A deletion actually dropped the schema.
- Two resources provisioned through different paths agree (equal table counts, equal shape).
- The pre-existing tenant still works, proving the change did not disturb existing data.

## Reporting

State the pass count, quote the decisive evidence rather than asserting success, and separate:
what the change fixed, what was already broken, and what you could not test in this environment
and why. If a probe assertion turns out to be wrong, say so plainly and move on — do not
reframe it as a product finding.

Promote a probe into a permanent test when it covers a contract others will rely on: pure
mapping or validation logic becomes a hermetic xUnit test; behavior that genuinely needs HTTP,
auth or storage becomes a Jest suite under `tests/functional/suites/`.
