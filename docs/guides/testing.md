# Testing Guide

This guide covers how to run and write tests for the XR5.0 Training Asset Repository.

## Test Suites

The project includes two test suites:

| Suite | Technology | Purpose |
|-------|------------|---------|
| Unit Tests | .NET / xUnit | Service and controller unit tests |
| Functional Tests | Node.js / Jest | End-to-end API verification |

## Quick Start

### Prerequisites

- .NET 8.0 SDK
- Node.js 20+
- Running API instance (for functional tests)

### Running All Tests

```bash
# Unit tests
dotnet test

# Functional tests
cd tests/functional
npm install
npm test
```

## Functional Tests

### Test Structure

```
tests/functional/
├── config.js              # Environment configuration
├── setup.js               # Global test setup
├── package.json           # Dependencies
├── helpers/
│   ├── api-client.js      # HTTP client with auth
│   └── test-data.js       # Test data factories
└── suites/
    ├── 01-health.test.js  # API health checks
    ├── 02-auth.test.js    # Authentication
    ├── 03-tenant.test.js  # Tenant CRUD
    ├── 04-storage.test.js # Storage validation
    ├── 05-materials.test.js # Material CRUD
    ├── 06-hierarchy.test.js # Material relationships
    ├── 07-programs.test.js  # Training programs
    └── 08-users.test.js     # User management
```

### Running Specific Test Suites

```bash
cd tests/functional

npm run test:health      # Health checks
npm run test:auth        # Authentication
npm run test:tenant      # Tenant operations
npm run test:storage     # Storage validation
npm run test:materials   # Material CRUD
npm run test:hierarchy   # Material relationships
npm run test:programs    # Training programs
npm run test:users       # User management

npm run test:verbose     # All tests with verbose output
```

### Configuration

Tests are configured via environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `API_URL` | `http://localhost:5286` | API base URL |
| `KEYCLOAK_URL` | `http://localhost:8180` | Keycloak server |
| `KEYCLOAK_REALM` | `xr50` | Keycloak realm |
| `KEYCLOAK_CLIENT` | `xr50-training-app` | Client ID |
| `TEST_USER` | `testuser` | Test username |
| `TEST_PASSWORD` | `testuser123` | Test password |
| `STORAGE_TYPE` | `S3` | Storage backend (S3/OwnCloud) |
| `S3_BUCKET` | `xr50-test` | S3 bucket name |
| `S3_REGION` | `eu-west-1` | AWS region |
| `S3_ENDPOINT` | _(empty)_ | Custom S3 endpoint (MinIO) |
| `EXISTING_TENANT` | _(empty)_ | Use existing tenant |
| `NO_AUTH` | `false` | Skip authentication |
| `DEBUG` | `false` | Enable verbose logging |
| `SKIP_CLEANUP` | `false` | Keep test resources |

### Common Test Scenarios

**Local development without authentication:**

```bash
NO_AUTH=true EXISTING_TENANT=dev-tenant npm test
```

**Testing with MinIO:**

```bash
STORAGE_TYPE=S3 S3_ENDPOINT=http://localhost:9000 npm test
```

**Debug a failing test:**

```bash
DEBUG=true SKIP_CLEANUP=true npm run test:materials
```

**Using an existing tenant:**

```bash
EXISTING_TENANT=my-tenant npm test
```

## Test Data Factories

The `test-data.js` helper provides factory functions for creating test objects:

### Materials

```javascript
const testData = require('./helpers/test-data');

testData.createVideoMaterial()       // Video with metadata
testData.createChecklistMaterial()   // Checklist with entries
testData.createWorkflowMaterial()    // Workflow with steps
testData.createChatbotMaterial()     // Chatbot configuration
```

### Users

```javascript
testData.createTestUser()            // Regular user
testData.createAdminUser()           // Admin user
```

### Tenants

```javascript
testData.createS3Tenant()            // S3 storage
testData.createOwnCloudTenant()      // OwnCloud storage
testData.createMinioTenant()         // MinIO (S3-compatible)
```

### Training Programs

```javascript
testData.createTrainingProgram()     // Basic program
testData.createProgramWithPaths()    // Program with learning paths
```

## API Client

The `api-client.js` provides authenticated HTTP methods:

```javascript
const apiClient = require('./helpers/api-client');

// Authenticate (optional with NO_AUTH=true)
await apiClient.authenticate(username, password);

// Material operations
await apiClient.createMaterial({ name: 'Test', type: 'Video' });
await apiClient.getMaterial(id);
await apiClient.updateMaterial(id, { name: 'Updated' });
await apiClient.deleteMaterial(id);

// User operations
await apiClient.createUser({ userName: 'test', password: 'pass' });
await apiClient.getUser('test');
await apiClient.updateUser('test', { fullName: 'Test User' });
await apiClient.deleteUser('test');

// File uploads
await apiClient.uploadFile(url, filePath);
await apiClient.uploadBuffer(url, buffer, filename);
```

## Writing New Tests

### Test Pattern

Follow the existing test structure:

```javascript
const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

describe('Feature Name', () => {
  let createdId;

  beforeAll(async () => {
    // Authenticate if needed
    if (!config.NO_AUTH) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }
  });

  afterAll(async () => {
    // Cleanup created resources
    if (createdId && !config.SKIP_CLEANUP) {
      try {
        await apiClient.deleteResource(createdId);
      } catch (error) {
        // Ignore cleanup errors
      }
    }
  });

  describe('Create Operations', () => {
    test('can create resource', async () => {
      const data = testData.createTestResource();
      const response = await apiClient.createResource(data);

      // Accept multiple valid status codes
      expect([200, 201]).toContain(response.status);

      if (response.status === 201) {
        expect(response.data).toHaveProperty('id');
        createdId = response.data.id;
      }
    });
  });

  describe('Validation', () => {
    test('rejects invalid input', async () => {
      const response = await apiClient.createResource({});

      // Expect validation error
      expect([400, 422]).toContain(response.status);
    });
  });
});
```

### Best Practices

1. **Use flexible assertions** - Accept multiple valid status codes when the API behavior might vary
2. **Clean up resources** - Delete created resources in `afterAll` unless `SKIP_CLEANUP` is set
3. **Track created resources** - Store IDs for cleanup and verification
4. **Log failures** - Use helper functions to log request/response for debugging
5. **Skip gracefully** - Check prerequisites and skip tests that can't run

### Debugging Tips

- Set `DEBUG=true` to see all request/response details
- Set `SKIP_CLEANUP=true` to inspect created resources after tests
- Use `npm run test:verbose` for detailed Jest output
- Check the API logs for server-side errors

## Unit Tests

### Running Unit Tests

```bash
# All unit tests
dotnet test

# Specific project
dotnet test tests/XR50TrainingAssetRepo.Tests

# With coverage
dotnet test --collect:"XPlat Code Coverage"

# Filter by name
dotnet test --filter "FullyQualifiedName~MaterialService"
```

### Test Location

```
tests/
└── XR50TrainingAssetRepo.Tests/
    ├── UnitTest1.cs
    └── xr50_unit_tests.cs
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Tests

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'
      - run: dotnet test

  functional-tests:
    runs-on: ubuntu-latest
    services:
      api:
        image: your-api-image
        ports:
          - 5286:5286
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
      - run: cd tests/functional && npm install
      - run: cd tests/functional && NO_AUTH=true npm test
        env:
          API_URL: http://localhost:5286
```
