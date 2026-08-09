# Design direction: identity, authorization and deployment modes

**Status: proposed — not scheduled.** Nothing here is implemented. This records a direction and,
just as importantly, why it should be approached in small backward-compatible steps rather than
as one change. Pilots are running against the current behaviour; none of the stages below may
break them.

## Why this document exists

The service has two front doors — the XR5.0 Hub session token and an OIDC/JWT bearer — and they
disagree about where authorization comes from. That disagreement is currently invisible because
the second door is labelled "development only". As soon as non-Hub deployment is taken seriously,
the disagreement becomes a design problem rather than a curiosity.

## The two audiences

**Horizon EU pilots, integrating through the XR5.0 Hub.** The Hub is a partner's commercial
platform. Its token is fixed: it carries identity and deliberately no roles, and it changes on
corporate timescales, not ours. We integrate on its terms. Identity drift between the Hub and our
user table (a Hub user with no local row, a local row still holding a grant after the Hub user is
gone) is a known and accepted cost for the pilot phase.

**Open-source adopters, running without the Hub.** People adopting an MIT-licensed component are
generally not also buying a B2B identity platform. For them the non-Hub path is not a fallback,
it is the product. They will bring whatever IdP they already run — Keycloak, Authentik, Zitadel,
a corporate SSO — or would prefer to bring none at all.

These two audiences pull in the same direction on the core question, which is convenient. They
pull in different directions on packaging, which is where the care is needed.

## Where the pilots actually are

| Pilot | Auth today | Bearing on this plan |
|---|---|---|
| 1, 4, 5 | XR5.0 Hub | Already on database-owned roles. Unaffected by everything below |
| 3 | Standalone, **no auth wired yet** | The forcing function — see below |
| 2, 6 | Undecided | No constraint yet; avoid deciding for them |

Two consequences follow, and they point in opposite directions.

**Nobody depends on Keycloak-issued role claims.** The deprecation risk that would normally make
this a slow, carefully-staged migration does not exist. There is no installed base to protect and
no partner to coordinate a cutover with.

**But pilot 3 is about to create one.** They are the first standalone deployment to wire up
authentication, and whatever they adopt becomes the de facto reference for every open-source
adopter after them. If they wire token-carried roles now, we manufacture exactly the migration
burden this document is trying to avoid — and we will be asking a partner to redo working
integration code. If the non-Hub path is ready first, the target design gets validated by a real
pilot instead.

That makes this less urgent than a deadline and more urgent than a backlog item: the window is
"before pilot 3 wires auth", and it is the only hard scheduling constraint here.

## Question 1 — where do roles belong?

**Direction: the local database is the source of truth for roles. The token asserts identity and
tenant scope, and nothing more.**

This is already how the Hub path works (`HubIdentityEnricher` resolves the tenant from the token's
`tenantId` and reads roles from `TenantAdmins` and `Users.admin`). It is not how the JWT path
works: there, roles and `tenantName` come straight from token claims and the local database is
never consulted.

The reasoning, in order of weight:

**It is the only model that is IdP-agnostic.** If roles ride in the token, our permission model
must be expressible in whatever IdP each adopter happens to run, and configurable enough to match
each one's claim shape. If roles live in our database, the IdP only has to assert who the caller
is — the one thing every OIDC provider does identically. `IamOptions` already states the ambition
("provider-agnostic … rather than hardcoded to any one provider's token shape") but only delivers
configurable *claim names*; the provider is still required to carry roles at all.

**Our role model is relational; token claims are flat.** `TenantAdmins` is keyed
`(TenantName, UserName)` — a genuine many-to-many. A user can be an administrator of one tenant
and an ordinary member of another. A flat `role` claim cannot express that. The bundled Keycloak
realm sidesteps it by giving each user exactly one `tenantName` attribute, which silently assumes
one tenant per user and will fail as a confusing `403` the first time that is untrue.

**The production IdP structurally cannot carry roles.** The Hub issues none, by contract. So
"roles in the token" is not available on the path that matters most today; choosing it for the
JWT path means maintaining two authorization models for one product, and testing the one we do
not ship.

**Revocation stays prompt.** Database roles take effect on the next request. Token-carried roles
persist until the token expires — with the Hub, a lifetime we do not control. The Hub decrypt
cache already bounds revocation latency deliberately at `XR50Hub:CacheSeconds`; token roles would
replace that bound with the token lifetime.

**It keeps the accepted drift on its safe side.** A user removed in the Hub can no longer obtain a
token, so any lingering local row and its grants become unreachable. The grant survives; the
ability to use it does not. The dangerous direction — permissions in circulation that we cannot
withdraw — only exists if roles ride in tokens.

### What it costs

A database read per authenticated request, making the tenant database a dependency of
authentication. Both are already true on the Hub path, and the decrypt cache is the model for
bounding the cost. Roles also have to be administered per deployment rather than centrally — but
that surface already exists (`PUT api/{tenantName}/users/{userName}/role`).

### When the opposite answer would be right

If the IdP were ours, were the system of record for organisational structure, and the roles were
coarse and global rather than per-tenant — especially with several services needing the same
roles. None of those hold. If that changes, the thing to reach for is group/entitlement claims
mapped to local roles, not application-specific roles minted by a partner's IdP.

### Note on materialisation

"Roles from the database" does not mean the authorization handlers run SQL. They read role
*claims*; enrichment materialises database state into claims once, at authentication. The claim
is a per-request cache of our own data, not an assertion by the IdP. `HubIdentityEnricher`
already has this shape.

## Question 2 — what decides a deployment's capabilities?

**Direction: configuration, not `ASPNETCORE_ENVIRONMENT`.**

Today a single environment flag bundles decisions that are unrelated to each other: whether the
JWT scheme is registered at all, whether Swagger is served, and (until recently) whether the
anonymous authorization bypass is live. The practical consequence is that an adopter running
without the Hub must set `ASPNETCORE_ENVIRONMENT=Development` permanently, and thereby accepts
the rest of the bundle.

That is survivable for someone tinkering with an open-source component. It is not survivable for
a corporate adopter who has to explain to a security reviewer why production runs in Development
mode. The current split works only because the audience we imagined was the first kind.

**Environment is not a security boundary.** Each capability should be independently switchable:

| Capability | Decided by |
|---|---|
| Hub session token scheme | a Hub shared secret being configured |
| OIDC/JWT bearer scheme | an IdP authority being configured *and* explicitly enabled |
| Swagger UI | its own setting, defaulting off |
| Anonymous authorization bypass | its own setting, defaulting off (already true) |

Both schemes, either, or neither — chosen by what is configured, with the selector continuing to
route by header presence.

> **Trap to avoid.** Keying JWT registration purely on "an authority is configured" is not safe as
> things stand: `appsettings.json` ships a default `IAM:Authority` pointing at
> `localhost:8180`. Every deployment would silently register the scheme. Registration needs an
> explicit enable flag, or that default has to be removed first. This is the single easiest way to
> get this change wrong.

## Question 3 — how does a fresh non-Hub install bootstrap?

This gap exists today, independently of everything above, and it is the one that most directly
blocks open-source adoption.

`TenantCreatorHandler` grants tenant creation to `IsSystemAdmin || IsHubAuthenticated`. A Hub user
therefore self-provisions and becomes their own tenant's administrator — a fresh Hub-backed
deployment bootstraps with nobody pre-seeded, exactly as `guides/authentication.md` describes. A
JWT user gets none of that: they need `systemadmin`, and the only way to obtain it today is to
hand-edit the Keycloak realm so the token carries the claim.

So **roles-in-token is currently the open-source bootstrap mechanism.** Removing it without
replacing it would leave no way into a fresh non-Hub installation at all.

It is worse than an asymmetry, though: on a fresh install it is a deadlock. `HubIdentityEnricher`
opens a *tenant-scoped* connection (`TenantConnectionString.ForDatabase`) and reads `Users` from
inside a tenant's own database, so the `Users.admin` system-admin flag can only exist within some
tenant. An installation with zero tenants therefore has nowhere for a system administrator to
live — and without a system administrator, `TenantCreator` refuses to create the first tenant. Hub
identities escape this only because `IsHubAuthenticated()` bypasses the system-admin requirement
entirely; that escape hatch is the sole reason a fresh Hub deployment bootstraps at all.

A standalone install has no such hatch. Whatever is decided about roles, this must be closed on
its own merits, and it is the first thing pilot 3 will hit. Options worth weighing:

- First authenticated identity becomes system administrator, behind an explicitly-enabled flag
  that the deployment turns off afterwards.
- A seeding command or startup-time configuration that names an initial administrator.
- Extending self-service tenant provisioning to any authenticated principal, with the new tenant
  bound to something stable from the token.

None is obviously best; the choice interacts with how adopters are expected to install.

## Staging

Every stage is additive and independently revertible. Pilots 1, 4 and 5 are on the Hub path and
are untouched by all of it.

| # | Step | Risk | Notes |
|---|---|---|---|
| 0 | *(done)* `ASPNETCORE_ENVIRONMENT` takes effect; bypass off by default | — | Prerequisite for anything below being meaningful |
| 1 | Bootstrap path for non-Hub installs (question 3) | low | Additive; nothing depends on the absence of a bootstrap. Unblocks pilot 3 on its own |
| 2 | JIT provisioning for JWT identities, mirroring `XR50Hub:AutoProvisionUsers` | low | Purely additive: fills the roster and satisfies the `UserMaterialData`/`UserMaterialScore` foreign keys to `Users`. Valuable whatever happens to roles |
| 3 | Database-owned roles for JWT identities, reusing the enricher's lookup | low–medium | No partner migration to perform (see below). Real cost is our own test fixtures |
| 4 | Decouple scheme registration and Swagger from environment (question 2) | medium | Mind the `IAM:Authority` default trap above. Needs a release note |
| 5 | Remove role mappers from the bundled realm | low | Cleanup, and closes a privilege-escalation surface: `HasAnyRole` reads every `role` claim, so anyone able to edit the realm can currently mint themselves `systemadmin` |

Stages 1 and 2 are the cheapest, the safest, and the ones pilot 3 needs. They are worth doing even
if nothing else here is ever scheduled.

### Why there is no `RolesSource` compatibility flag

An earlier draft proposed `IAM:RolesSource = Token | Database`, defaulting to `Token`, as a
migration lever. With the pilot picture known, that is the wrong call:

- **There is nothing to migrate.** No pilot uses Keycloak-issued role claims. A flag whose
  compatibility value nobody selects is dead weight.
- **It would preserve two authorization models** — the exact condition this document exists to
  remove — and double the authorization test matrix permanently.
- **It is the same species of setting as `AllowAnonymousInDevelopment`**: a configuration knob
  that silently relocates where authorization comes from. That pattern has already cost this
  project one serious defect (see the test plan's finding 1). Adding a second one, in the same
  subsystem, in the same release cycle, is not a trade worth making.

Go directly to database-owned roles for the JWT path.

### The real cost of stage 3

Not partner migration — our own verification. The functional suite and `scripts/authz-probe.sh`
both authenticate as `sysadmin`, `tenantadmin`, `admin` and `testuser` and rely on the realm's
`role` claims to reach each policy tier. Under database-owned roles those four identities need
corresponding `Users` rows and `TenantAdmins` grants seeded per tenant before they carry any
authority. That is a fixture change across `tests/functional/setup.js`, the probe, and the plan's
stage-0 provisioning step. Straightforward, but it should be budgeted rather than discovered.

## Explicitly not proposed

- **Writing users or roles into the IdP.** Neither a hook on user creation nor a batch sync. It
  inverts the ownership split, requires privileged IdP admin credentials in application
  configuration, and creates two independently mutable stores needing reconciliation. Under
  database-owned roles there is nothing to synchronise.
- **Changing anything about the Hub token or its contract.** Out of our control by design.
- **A big-bang cutover.** The whole point of the staging table.

## Open questions

- Which bootstrap option (question 3) fits how adopters are expected to install? This is the one
  blocking pilot 3, so it wants an answer first.
- Should `tenantName` for JWT principals keep coming from a token claim, or resolve through the
  registry the way the Hub's `tenantId` does? Keeping the claim is simpler; resolving it is more
  consistent and would let one identity span tenants — which the flat `tenantName` attribute in
  the bundled realm cannot express today.
- Is there an appetite for supporting no IdP at all for the smallest open-source installs, or is
  "bring your own OIDC provider" an acceptable floor?
- Pilots 2 and 6 are undecided. Nothing here should narrow their options; if either arrives with
  an existing IdP carrying its own roles, that is the case that would reopen the group- or
  entitlement-claim mapping noted under question 1.

*Answered:* no pilot depends on Keycloak-issued role claims — pilots 1, 4 and 5 are on the Hub,
pilot 3 has not wired authentication yet, and pilots 2 and 6 are undecided.

## Related

- `guides/authentication.md` — how both schemes behave today
- `guides/authorization-test-plan.md` — the verification plan and the findings that prompted this
- `Infrastructure/Auth/` — `HubIdentityEnricher`, `TenantAuthorizationHandlers`, `IamOptions`
