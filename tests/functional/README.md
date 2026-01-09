# XR5.0 Functional Verification Tests

Functional test suite for verifying XR5.0 Training Asset Repository installations.

## Overview

This test suite runs against a live API to verify:
- API health and availability
- Keycloak authentication flow
- Tenant provisioning with S3 storage
- S3 file operations (upload, download, delete)
- Material CRUD operations
- Material hierarchy and relationships
- Training program management
- User management

## Prerequisites

- Node.js 18+
- Access to a running XR5.0 API instance
- (Optional) Keycloak instance for authentication tests
- (Optional) S3 bucket or MinIO for storage tests

## Installation

```bash
cd tests/functional
npm install
```

## Quick Start

### Run against local sandbox

```bash
# Start the sandbox first
docker-compose --profile sandbox up -d

# Run tests
npm test
```

### Run against AWS environment

```bash
API_URL=https://api.your-domain.com \
KEYCLOAK_URL=https://auth.your-domain.com \
S3_BUCKET=your-bucket-name \
TEST_USER=your-user \
TEST_PASSWORD=your-password \
npm test
```

## Configuration

All settings can be configured via environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `API_URL` | `http://localhost:5286` | Base URL of the XR5.0 API |
| `KEYCLOAK_URL` | `http://localhost:8180` | Keycloak server URL |
| `KEYCLOAK_REALM` | `xr50` | Keycloak realm name |
| `KEYCLOAK_CLIENT` | `xr50-training-app` | Keycloak client ID |
| `TEST_USER` | `testuser` | Username for authentication |
| `TEST_PASSWORD` | `testuser123` | Password for authentication |
| `ADMIN_USER` | `admin` | Admin username (for tenant operations) |
| `ADMIN_PASSWORD` | `admin123` | Admin password |
| `S3_BUCKET` | `xr50-test-verification` | S3 bucket for storage tests |
| `S3_REGION` | `eu-west-1` | S3 bucket region |
| `S3_ENDPOINT` | (empty) | Custom S3 endpoint (for MinIO) |
| `EXISTING_TENANT` | (empty) | Use existing tenant instead of creating new |
| `SKIP_CLEANUP` | `false` | Skip cleanup of test resources |
| `DEBUG` | `false` | Enable debug logging |

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

# All tests with verbose output
npm run test:verbose
```

## Test Suites

### 1. Health Checks (`01-health.test.js`)
- GET /health returns healthy status
- Swagger documentation accessible

### 2. Authentication (`02-auth.test.js`)
- Token acquisition from Keycloak
- Protected endpoint access
- Invalid token rejection

### 3. Tenant Management (`03-tenant.test.js`)
- Create tenant with S3 configuration
- Validate storage connectivity
- Get storage statistics
- Delete tenant

### 4. S3 Storage (`04-storage.test.js`)
- Upload files to S3
- Download files from S3
- Verify file content integrity
- Delete files

### 5. Materials (`05-materials.test.js`)
- Create different material types (Video, Checklist, Workflow)
- Read material details
- Update materials
- Delete materials

### 6. Material Hierarchy (`06-hierarchy.test.js`)
- Assign parent-child relationships
- Query hierarchy
- Circular reference prevention

### 7. Training Programs (`07-programs.test.js`)
- Create programs with learning paths
- Assign materials to programs
- Program detail retrieval

### 8. Users (`08-users.test.js`)
- Create users (regular and admin)
- Update user profiles
- Delete users

## Debugging

Enable debug mode to see all API requests:

```bash
DEBUG=true npm test
```

Skip cleanup to inspect test data after failures:

```bash
SKIP_CLEANUP=true npm test
```

## Using with Existing Tenant

If you don't want to create a new tenant for each test run:

```bash
EXISTING_TENANT=my-production-tenant npm test
```

Note: Storage tests may fail if the tenant doesn't have proper S3 configuration.

## Expected Output

```
========================================
  XR5.0 Functional Test Suite
========================================

Configuration:
  API URL:      http://localhost:5286
  Keycloak:     http://localhost:8180
  Test Tenant:  verify-1704729600000
  S3 Bucket:    xr50-test-verification
  S3 Region:    eu-west-1

API connectivity: OK
Keycloak connectivity: OK

Starting tests...

 PASS  suites/01-health.test.js
 PASS  suites/02-auth.test.js
 PASS  suites/03-tenant.test.js
 PASS  suites/04-storage.test.js
 PASS  suites/05-materials.test.js
 PASS  suites/06-hierarchy.test.js
 PASS  suites/07-programs.test.js
 PASS  suites/08-users.test.js

Test Suites: 8 passed, 8 total
Tests:       45 passed, 45 total
Time:        12.345 s

========================================
  Cleanup
========================================

  Deleted tenant: verify-1704729600000

Cleanup complete: 1 resources deleted, 0 failed.
```

## Troubleshooting

### "Cannot reach API - aborting tests"
- Verify the API is running: `curl http://localhost:5286/health`
- Check the `API_URL` environment variable

### Authentication tests skipped
- Keycloak may not be running or accessible
- Check `KEYCLOAK_URL` and ensure the realm exists

### S3 upload fails
- Verify S3/MinIO credentials are correct
- Check bucket exists and has proper permissions
- For MinIO: ensure `S3_ENDPOINT` is set

### Tenant creation fails with 403
- You may need admin credentials
- Set `ADMIN_USER` and `ADMIN_PASSWORD`

## License

MIT - See main repository LICENSE file.
