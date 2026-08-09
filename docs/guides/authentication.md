# Authentication

The XR50 Training API accepts two authentication schemes, selected per request:

| Scheme | Header | Environments |
|--------|--------|--------------|
| XR5.0 Hub session token | `HL-Hub-Session-Token` | all (the only scheme outside Development) |
| Keycloak JWT bearer | `Authorization: Bearer` | Development only |

> This page describes current behaviour. The two schemes disagree about where roles come from —
> the Hub path reads them from our database, the JWT path from token claims — and the JWT path
> being Development-gated makes non-Hub deployment awkward for anyone who cannot run in
> Development. A proposed direction for both is recorded in
> [Identity and Authorization Direction](../design/identity-and-authorization-direction.md).
> Nothing there is implemented; this document remains authoritative for how the system behaves.

## What each role may do

| | `member` | `tenantadmin` | `systemadmin` |
|---|---|---|---|
| Read training content (materials, assets, learning paths, programs) | ✔ | ✔ | ✔ |
| Create / update / delete content | | ✔ | ✔ |
| Read and write **own** progress (quiz submissions, completions) | ✔ | ✔ | ✔ |
| Read another user's progress, or the tenant-wide progress view | | ✔ | ✔ |
| Manage tenant users and grant `tenantadmin` | | ✔ | ✔ |
| Set the system-admin flag, list/delete tenants, troubleshooting API | | | ✔ |

Progress is always recorded against the caller's own identity - no endpoint accepts a user id to
write against - and reading someone else's records requires a tenant-administration role.
Tenant-scoped policies additionally require the token's `tenantName` to match the `{tenantName}`
route segment; system administrators are exempt from that match.

## XR5.0 Hub Session Token (production)

Every request from the XR5.0 Hub to this service carries an encrypted, opaque session token in
the `HL-Hub-Session-Token` header (spec: *XR5.0 Hub Session Token — External Service
Integration*). The token is a **bearer credential**: accept it only over TLS, never log it, and
never place it in a URL.

### Validation flow

1. `HubSessionTokenAuthenticationHandler` reads the header and calls the Hub decrypt API
   (`POST {XR50Hub:BaseUrl}/api/v1/session-token/decrypt`) through `HubSessionTokenService`,
   authenticating with the shared secret (`hl-hub-external-service-secret` header, from
   `XR50Hub:SharedSecret`).
2. Only a response with `valid: true` authenticates. `valid: false`
   (`MALFORMED | EXPIRED | SESSION_INACTIVE`), a rejected secret, or a missing token all fail
   closed with `401`. If the Hub is unreachable the request fails with `503`.
3. Decrypt results are cached in-memory for `XR50Hub:CacheSeconds` (default 60 s), keyed by a
   SHA-256 hash of the token and never beyond the token's `expiresAt`. This bounds revocation
   latency: a session revoked on the Hub stays usable here for at most `CacheSeconds`.

### Claim mapping

| Hub claim | Emitted claim | Consumed by |
|-----------|---------------|-------------|
| `userId` | `sub` / NameIdentifier; primary local-user join key | role lookup, audit |
| `user.email` | `email` | fallback local-user join, fallback attribution |
| matched local `UserName` (else email, else userId GUID) | `preferred_username` | progress records, `GetUserId()` |
| `tenantId` → registry lookup | `tenantName` | tenant-route authorization |
| local DB roles | `role` (`tenantadmin` / `systemadmin`) | policy handlers |
| `sessionId`, `applicationId`, `user.skillLevel` | same-named claims | available to controllers |

The Hub authenticates the user; **authorization stays grounded in our own registry**
(`HubIdentityEnricher`):

- **Tenant**: the token's `tenantId` GUID is resolved against `XR50TenantRegistry.HubTenantId`.
  Set the mapping per tenant with `PUT xr50/trainingAssetRepository/Tenants/{tenantName}/hub-tenant`
  (SystemAdmin) or at tenant creation (`hubTenantId` field). An unmapped `tenantId` still
  authenticates but carries no `tenantName`, so tenant-scoped endpoints return `403`.
- **Self-service tenant provisioning**: any Hub-authenticated user may `POST Tenants` to create
  the tenant for their *own* Hub tenant (`TenantCreator` policy). The new tenant is force-bound
  to the caller's token `tenantId` (any caller-supplied `hubTenantId` is ignored), at most one
  local tenant may exist per Hub tenant (`409` otherwise), the owner's system-admin flag is
  stripped, and the creator is seeded as the tenant's admin (`TenantAdmins` row matched by their
  Hub e-mail). This makes a fresh Hub-hosted deployment bootstrappable without any pre-seeded
  admin: the first user of a pilot provisions and manages their own tenant. Tenant deletion and
  re-mapping stay SystemAdmin-only, as does creation for JWT/Keycloak principals.
- **User/roles**: the Hub identity is joined to the tenant DB's `Users` primarily by the Hub
  `userId` GUID against `UserName` (provision Hub users - especially e-mail-less service
  accounts - with `UserName` = their Hub userId), falling back to a case-insensitive e-mail
  match for human users provisioned by address. `TenantAdmins` membership grants `tenantadmin`;
  `Users.admin` grants `systemadmin`. On the e-mail fallback, duplicates resolve to the first
  user by `UserName`.

### Keeping users in step with the Hub

The Hub session token deliberately carries **no roles** - the Hub operator keeps identity, we
keep permissions - so the same user has to exist on both sides. Three things make that
practical without anyone transcribing GUIDs:

1. **Just-in-time provisioning.** The first request of a Hub identity whose tenant is mapped but
   who has no local row creates one: `UserName` = the Hub `userId`, display name and e-mail from
   the token, no password, **no roles**. New arrivals therefore show up in the roster as plain
   members, and an administrator only has to grant a role. Turn it off with
   `XR50Hub:AutoProvisionUsers=false` (`XR50HUB_AUTO_PROVISION_USERS`) if a deployment prefers
   users to be pre-provisioned; unknown identities then authenticate with no tenant role.
   Existing GUID-keyed rows have their display name and e-mail refreshed from the token when the
   Hub profile changes - the Hub owns the profile, we own the roles.
2. **`GET api/auth/me`** reports what the local side made of the credential: authentication
   scheme, Hub `userId` and `tenantId`, the local user it joined to, the mapped tenant and the
   effective role. This is the shortest path to the Hub user id needed for a grant, and the
   first thing to check when a token authenticates but authorization surprises you.
3. **`PUT api/{tenantName}/users/{userName}/role`** (TenantAdmin) with body
   `{"role": "member" | "tenantadmin"}` is where a Hub identity becomes a tenant administrator.
   The two roles match the two access levels the pilots asked for: `member` reads content and
   records its own progress and quiz scores; `tenantadmin` has full access within the tenant,
   including authoring and user management. `GET api/{tenantName}/users` lists every user with
   its `role`; the stored password is never part of a response.

System administration (`Users.admin`) is **not** grantable from the tenant surface: it crosses
tenant boundaries, and any Hub user can provision their own tenant and become its admin. Only a
system administrator can set that flag, through `POST`/`PUT api/{tenantName}/users`. Deleting a
user also drops their role grants, so a re-provisioned Hub user id never inherits an old one.

Pre-provisioning ahead of first login works too: `POST api/{tenantName}/users` with
`userName` = the Hub `userId`. A password is only required for OwnCloud-backed tenants, which
mirror users into their own account store; Hub-authenticated identities (service accounts in
particular) never need one.

### Configuration

```jsonc
"XR50Hub": {
  "BaseUrl": "https://platform.xr50.eu",   // decrypt API host, HTTPS required outside Development
  "SharedSecret": "",                      // provided by the Hub operator out of band, env-only
  "DevelopmentToken": "",                  // fixed dev token, honored ONLY in Development
  "CacheSeconds": 60,
  "TimeoutSeconds": 5,
  "AutoProvisionUsers": true               // create a local user row on first sight of a Hub identity
}
```

Docker: `XR50HUB_BASE_URL`, `XR50HUB_SHARED_SECRET`, `XR50HUB_DEV_TOKEN`,
`XR50HUB_AUTO_PROVISION_USERS` in `.env`
(see `.env.example`). Secrets are never committed.

### Development token

For local development the Hub operator provides a fixed token value. Set it as
`XR50Hub:DevelopmentToken` (or `XR50HUB_DEV_TOKEN`); requests presenting exactly that value are
authenticated as the spec's fixed identity (user `Dev Tester`, `dev-test@holo-light.com`, tenant
id `976092b0-0ca8-404d-99b8-30a8c755719c`) without calling the decrypt API, then flow through the
normal tenant-mapping and role lookup. The short-circuit is double-gated — Development
environment **and** a configured token — so it cannot activate in production. Map the dev tenant
id to a local tenant via the hub-tenant endpoint to give the dev identity a tenant scope.

---

# Keycloak Authentication Setup (Development only)

Keycloak is the Development stand-in IdP for the JWT bearer scheme; it is not registered outside
Development. This section describes how to set it up and test with it.

## Quick Start

### 1. Start Keycloak

```bash
# Start Keycloak with default profile
docker-compose up keycloak -d

# Or with lab profile (includes OwnCloud)
docker-compose --profile lab up -d
```

Keycloak will be available at: **http://localhost:8180**

### 2. Access Admin Console

- URL: http://localhost:8180/admin
- Username: `admin`
- Password: `admin`

The `xr50` realm is automatically imported with test users and clients.

## Pre-configured Test Users

| Username     | Password         | Roles                | Tenant       |
|-------------|------------------|----------------------|--------------|
| testuser    | testuser123      | user                 | test_company |
| admin       | admin123         | admin, user          | test_company |
| tenantadmin | tenantadmin123   | tenantadmin, user    | test_company |

## Pre-configured Clients

| Client ID          | Type    | Purpose                              |
|-------------------|---------|--------------------------------------|
| xr50-training-api | Bearer  | Backend API (bearer-only)            |
| xr50-training-app | Public  | Frontend application                 |
| xr50-swagger      | Public  | Swagger UI authentication            |

## Getting a Token

### Using PowerShell Script

```powershell
# Get token for testuser
.\scripts\get-keycloak-token.ps1

# Get token for admin
.\scripts\get-keycloak-token.ps1 -Username admin -Password admin123
```

### Using Bash Script

```bash
# Get token for testuser
./scripts/get-keycloak-token.sh

# Get token for admin
./scripts/get-keycloak-token.sh admin admin123
```

### Using curl

```bash
# Get access token
curl -X POST http://localhost:8180/realms/xr50/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=xr50-training-app" \
  -d "username=testuser" \
  -d "password=testuser123"
```

## Using Swagger UI

1. Start the API: `dotnet run`
2. Open Swagger: http://localhost:5286/swagger
3. Click the "Authorize" button
4. Choose either:
   - **OAuth2 (password)**: Enter username/password directly
   - **Bearer**: Paste a token obtained from scripts

### OAuth2 Password Flow in Swagger

1. Click "Authorize"
2. Under "oauth2 (OAuth2, password)", enter:
   - Username: `testuser`
   - Password: `testuser123`
3. Click "Authorize"

## API Endpoints with Authentication

The following endpoints require authentication:

- `POST /api/{tenantName}/materials/{materialId}/submit` - Submit quiz answers

Example authenticated request:

```bash
TOKEN=$(./scripts/get-keycloak-token.sh | grep "Access Token" -A1 | tail -1)

curl -X POST "http://localhost:5286/api/test_company/materials/1/submit" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "answers": [
      {"questionId": 1, "selectedAnswerIds": [1]},
      {"questionId": 2, "selectedAnswerIds": [3]}
    ]
  }'
```

## Token Claims

The JWT token includes these claims that are used by the API:

| Claim              | Description                    | Example                           |
|-------------------|--------------------------------|-----------------------------------|
| sub               | Subject (user ID)              | `1234-5678-uuid`                  |
| preferred_username| Username                       | `testuser`                        |
| email             | User email                     | `testuser@xr50.local`             |
| tenantName        | Tenant the user belongs to     | `test_company`                    |
| role              | User roles                     | `["user"]`                        |

The API extracts user ID from claims in this order:
1. `nameidentifier` (ClaimTypes.NameIdentifier)
2. `sub`
3. `preferred_username`
4. `email`
5. `name`

## Development Mode

In development mode, you can bypass authentication by setting in `appsettings.Development.json`:

```json
{
  "IAM": {
    "AllowAnonymousInDevelopment": true,
    "DevelopmentUserId": "dev-test-user"
  }
}
```

This allows testing the API without a valid token. The fallback user ID will be used instead.

**Note:** Set `AllowAnonymousInDevelopment: false` to require real authentication in development.

## Docker Environment

When running in Docker with docker-compose, the API connects to Keycloak using the internal Docker network:

```yaml
IAM__Authority: http://keycloak:8080/realms/xr50
IAM__Issuer: http://localhost:8180/realms/xr50  # External issuer for token validation
```

**Important:** The issuer in the token must match the external URL (`localhost:8180`) because tokens are obtained from outside Docker.

## Troubleshooting

### Token validation fails with "issuer mismatch"

Make sure the `IAM:Issuer` setting matches how the token was obtained:
- If token was obtained from `localhost:8180`, issuer should be `http://localhost:8180/realms/xr50`
- If token was obtained from `keycloak:8080` (inside Docker), issuer should be `http://keycloak:8080/realms/xr50`

### "User identifier not found in token"

Check the token claims by decoding it:
```bash
# Decode JWT payload
echo $TOKEN | cut -d'.' -f2 | base64 -d | jq .
```

Ensure one of the expected claims (sub, preferred_username, email) is present.

### Keycloak won't start

Check logs:
```bash
docker-compose logs keycloak
```

Common issues:
- Port 8180 already in use
- Realm import file syntax errors

### Reset Keycloak data

```bash
docker-compose down
docker volume rm training-repo_keycloak_data
docker-compose up keycloak -d
```

## Production Configuration

For production, update `appsettings.json`:

```json
{
  "IAM": {
    "Authority": "https://your-keycloak.example.com/realms/xr50",
    "MetadataEndpoint": "https://your-keycloak.example.com/realms/xr50/.well-known/openid-configuration",
    "Issuer": "https://your-keycloak.example.com/realms/xr50",
    "Audience": "xr50-training-api",
    "RequireHttpsMetadata": true,
    "AllowAnonymousInDevelopment": false
  }
}
```
