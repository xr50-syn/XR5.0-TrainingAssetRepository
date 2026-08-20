# Run Tests Against Server

Use this skill when running functional tests against a running XR5.0 API server.

## Quick Start

### Prerequisites

1. **API server must be running** (locally or remote)
2. **S3 bucket must exist** and be accessible (tests create a tenant with S3 storage)
3. **Node.js installed** and dependencies installed:
   ```bash
   cd tests/functional
   npm install
   ```

### Test Flow

The tests automatically:
1. **Setup**: Create a test tenant (or use existing if `EXISTING_TENANT` is set)
2. **Run**: Execute all tests in order (health, auth, tenant, storage, materials, hierarchy, programs, users)
3. **Teardown**: Delete the test tenant (unless `SKIP_CLEANUP=true`)

### Run Without Authentication (Default for Development)

```cmd
REM Windows Command Prompt
cd tests\functional
set NO_AUTH=true && set API_URL=http://localhost:5286 && npm test
```

```powershell
# Windows PowerShell
cd tests/functional
$env:NO_AUTH = "true"
$env:API_URL = "http://localhost:5286"
npm test
```

```bash
# Linux/macOS
cd tests/functional
NO_AUTH=true API_URL=http://localhost:5286 npm test
```

### Run With Authentication (Keycloak)

```cmd
REM Windows Command Prompt
cd tests\functional
set API_URL=http://localhost:5286 && set KEYCLOAK_URL=http://localhost:8180 && set TEST_USER=testuser && set TEST_PASSWORD=testuser123 && npm test
```

```powershell
# Windows PowerShell
cd tests/functional
$env:API_URL = "http://localhost:5286"
$env:KEYCLOAK_URL = "http://localhost:8180"
$env:TEST_USER = "testuser"
$env:TEST_PASSWORD = "testuser123"
npm test
```

---

## Environment Variables

### Required

| Variable | Default | Description |
|----------|---------|-------------|
| `API_URL` | `http://localhost:5286` | Base URL of the XR5.0 API |

### Authentication

| Variable | Default | Description |
|----------|---------|-------------|
| `NO_AUTH` | `false` | Set to `true` to skip Keycloak authentication |
| `KEYCLOAK_URL` | `http://localhost:8180` | Keycloak server URL |
| `KEYCLOAK_REALM` | `xr50` | Keycloak realm name |
| `KEYCLOAK_CLIENT` | `xr50-training-app` | Keycloak client ID |
| `TEST_USER` | `testuser` | Username for authentication |
| `TEST_PASSWORD` | `testuser123` | Password for authentication |
| `ADMIN_USER` | `admin` | Admin username (for tenant operations) |
| `ADMIN_PASSWORD` | `admin123` | Admin password |

### S3/Storage

| Variable | Default | Description |
|----------|---------|-------------|
| `S3_BUCKET` | `xr50-test-verification` | S3 bucket for storage tests |
| `S3_REGION` | `eu-west-1` | S3 bucket region |
| `S3_ENDPOINT` | (empty) | Custom S3 endpoint (for MinIO) |

### Test Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `EXISTING_TENANT` | (empty) | Use existing tenant (skip creation/deletion) |
| `TEST_TENANT` | `test-{timestamp}` | Custom test tenant name (auto-generated if not set) |
| `SKIP_CLEANUP` | `false` | Keep test tenant after run (for debugging) |
| `DEBUG` | `false` | Enable verbose request/response logging |

---

## Common Scenarios

### Local Development (No Auth, Local API)

```cmd
set NO_AUTH=true && set API_URL=http://localhost:5286 && npm test
```

### Local with MinIO Storage

```cmd
set NO_AUTH=true && set API_URL=http://localhost:5286 && set S3_ENDPOINT=http://localhost:9000 && set S3_BUCKET=xr50-dev && npm test
```

### Remote Server (AWS)

```cmd
set API_URL=https://api.your-domain.com && set KEYCLOAK_URL=https://auth.your-domain.com && set S3_BUCKET=your-production-bucket && set TEST_USER=your-user && set TEST_PASSWORD=your-password && npm test
```

### Use Existing Tenant (Skip Tenant Creation)

```cmd
set NO_AUTH=true && set API_URL=http://localhost:5286 && set EXISTING_TENANT=my-tenant && npm test
```

### Debug Mode (See All Requests)

```cmd
set NO_AUTH=true && set API_URL=http://localhost:5286 && set DEBUG=true && npm test
```

### Keep Test Data After Run

```cmd
set NO_AUTH=true && set API_URL=http://localhost:5286 && set SKIP_CLEANUP=true && npm test
```

---

## Running Specific Test Suites

```bash
# Health checks only
npm run test:health

# Authentication tests
npm run test:auth

# Tenant and S3 validation
npm run test:tenant

# S3 storage operations
npm run test:storage

# Material CRUD
npm run test:materials

# Material hierarchy
npm run test:hierarchy

# Training programs
npm run test:programs

# User management
npm run test:users

# Verbose output
npm run test:verbose
```

---

## Troubleshooting

### "Unknown database 'xr50_tenant_...'"
- The test tenant was not created properly
- Ensure setup runs before other tests (tests run in alphabetical order)
- Check S3 bucket exists and API can connect to it
- Try with an existing tenant: `set EXISTING_TENANT=test-company`

### "Cannot reach API - aborting tests"
- Verify the API is running: `curl http://localhost:5286/health`
- Check the `API_URL` environment variable

### Authentication tests skipped
- Either set `NO_AUTH=true` or ensure Keycloak is running
- Check `KEYCLOAK_URL` and credentials

### S3 upload fails
- Verify S3/MinIO is running and accessible
- Check `S3_BUCKET` exists and has proper permissions
- For MinIO: ensure `S3_ENDPOINT` is set

### Tenant creation fails with 403
- Set `ADMIN_USER` and `ADMIN_PASSWORD` with admin credentials

### Environment variable not working (Windows)
- No spaces around `=` in cmd: `set VAR=value` (correct) vs `set VAR = value` (wrong)
- Restart terminal after setting variables
- Verify with `echo %VAR%` (cmd) or `$env:VAR` (PowerShell)

---

## Files Reference

| File | Purpose |
|------|---------|
| `tests/functional/config.js` | Environment variable defaults, tenant name resolution |
| `tests/functional/setup.js` | Creates test tenant, saves state |
| `tests/functional/teardown.js` | Deletes test tenant, cleans up state |
| `tests/functional/testSequencer.js` | Ensures alphabetical test order |
| `tests/functional/helpers/api-client.js` | HTTP client with auth support |
| `tests/functional/suites/*.test.js` | Individual test suites (run in order) |
| `tests/functional/.test-state.json` | Shared state between setup/tests/teardown (auto-generated) |
