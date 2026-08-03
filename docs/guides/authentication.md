# Authentication

The XR50 Training API accepts two authentication schemes, selected per request:

| Scheme | Header | Environments |
|--------|--------|--------------|
| XR5.0 Hub session token | `HL-Hub-Session-Token` | all (the only scheme outside Development) |
| Keycloak JWT bearer | `Authorization: Bearer` | Development only |

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
| `userId` | `sub` / NameIdentifier | audit, fallback user id |
| `user.email` | `email`; also `preferred_username` when no local user matches | `GetUserId()` |
| matched local `UserName` | `preferred_username` | progress records, `GetUserId()` |
| `tenantId` → registry lookup | `tenantName` | tenant-route authorization |
| local DB roles | `role` (`tenantadmin` / `systemadmin`) | policy handlers |
| `sessionId`, `applicationId`, `user.skillLevel` | same-named claims | available to controllers |

The Hub authenticates the user; **authorization stays grounded in our own registry**
(`HubIdentityEnricher`):

- **Tenant**: the token's `tenantId` GUID is resolved against `XR50TenantRegistry.HubTenantId`.
  Set the mapping per tenant with `PUT xr50/trainingAssetRepository/Tenants/{tenantName}/hub-tenant`
  (SystemAdmin) or at tenant creation (`hubTenantId` field). An unmapped `tenantId` still
  authenticates but carries no `tenantName`, so tenant-scoped endpoints return `403`.
- **User/roles**: the Hub identity is joined to the tenant DB's `Users` by e-mail
  (case-insensitive). `TenantAdmins` membership grants `tenantadmin`; `Users.admin` grants
  `systemadmin`. Known limitation: e-mail is the join key (the Hub `userId` GUID is not stored
  locally); if several users share an e-mail the first by `UserName` wins.

### Configuration

```jsonc
"XR50Hub": {
  "BaseUrl": "https://platform.xr50.eu",   // decrypt API host, HTTPS required outside Development
  "SharedSecret": "",                      // provided by the Hub operator out of band, env-only
  "DevelopmentToken": "",                  // fixed dev token, honored ONLY in Development
  "CacheSeconds": 60,
  "TimeoutSeconds": 5
}
```

Docker: `XR50HUB_BASE_URL`, `XR50HUB_SHARED_SECRET`, `XR50HUB_DEV_TOKEN` in `.env`
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
