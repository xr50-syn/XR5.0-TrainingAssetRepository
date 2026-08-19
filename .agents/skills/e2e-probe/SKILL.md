---
name: e2e-probe
description: Design and run a targeted live probe for an XR5.0 repository change that existing suites do not cover, including controls, below-API assertions, and verified cleanup. Use for specific persistence, storage, authorization, migration, or tenant-provisioning behavior that needs end-to-end proof.
---

# Probe uncovered behavior

Read `docs/guides/verification-workflow.md`, especially "Writing a targeted probe"; it is
authoritative and wins if this adapter disagrees with it. Complete the appropriate green ladder
with `$e2e-verify` before probing.

Derive cases from the code diff. Include a case with the opposite expected outcome and an
unchanged control so assertion mistakes are distinguishable from product defects. Record a
baseline before changing live state.

Keep throwaway probe code outside the repository. Assert at the layer the change affects: an
HTTP success alone does not prove correct database or storage state. Clean up every resource the
probe creates, then compare final state with the recorded baseline.

Report assertion counts and decisive evidence. Identify skipped checks and distinguish findings
caused by the change from pre-existing behavior.
