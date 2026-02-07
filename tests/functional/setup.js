const fs = require('fs');
const path = require('path');

/**
 * Global test setup
 *
 * Runs once before all tests to:
 * 1. Generate and save test tenant name
 * 2. Verify API is reachable
 * 3. Create the test tenant (unless EXISTING_TENANT is set)
 * 4. Authenticate with Keycloak (if not NO_AUTH)
 */

module.exports = async function globalSetup() {
  // Import config after we potentially create the state file
  const axios = require('axios');

  // Base config values (before state file exists)
  const API_BASE_URL = process.env.API_URL || 'http://localhost:5286';
  const KEYCLOAK_URL = process.env.KEYCLOAK_URL || 'http://localhost:8180';
  const KEYCLOAK_REALM = process.env.KEYCLOAK_REALM || 'xr50';
  const S3_BUCKET = process.env.S3_BUCKET || 'xr50-test-verification';
  const S3_REGION = process.env.S3_REGION || 'eu-west-1';
  const S3_ENDPOINT = process.env.S3_ENDPOINT || '';
  const EXISTING_TENANT = process.env.EXISTING_TENANT || '';
  const NO_AUTH = process.env.NO_AUTH === 'true';
  const DEBUG = process.env.DEBUG === 'true';
  const SKIP_CLEANUP = process.env.SKIP_CLEANUP === 'true';

  // State file for sharing tenant name across test files
  const STATE_FILE = path.join(__dirname, '.test-state.json');

  // Generate test tenant name
  const testTenant = EXISTING_TENANT || process.env.TEST_TENANT || `test-${Date.now()}`;

  // Save state file so all test files use the same tenant
  const state = {
    testTenant,
    existingTenant: EXISTING_TENANT,
    createdTenant: !EXISTING_TENANT, // Track if we created it (for cleanup)
    createdAt: new Date().toISOString(),
    createdResources: {
      tenants: [],
      materials: [],
      assets: [],
      programs: [],
      users: []
    }
  };

  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));

  console.log('\n========================================');
  console.log('  XR5.0 Functional Test Suite');
  console.log('========================================\n');

  console.log('Configuration:');
  console.log(`  API URL:       ${API_BASE_URL}`);
  console.log(`  Keycloak:      ${KEYCLOAK_URL}`);
  console.log(`  Test Tenant:   ${testTenant}`);
  console.log(`  S3 Bucket:     ${S3_BUCKET}`);
  console.log(`  S3 Region:     ${S3_REGION}`);
  console.log(`  S3 Endpoint:   ${S3_ENDPOINT || '(default AWS)'}`);
  if (EXISTING_TENANT) {
    console.log(`  Mode:          Using EXISTING tenant`);
  } else {
    console.log(`  Mode:          Creating NEW tenant`);
  }
  if (NO_AUTH) {
    console.log(`  Auth Mode:     NO_AUTH (authentication disabled)`);
  }
  console.log(`  Debug Mode:    ${DEBUG ? 'ON' : 'OFF'}`);
  console.log(`  Skip Cleanup:  ${SKIP_CLEANUP ? 'YES' : 'NO'}`);
  console.log('');

  const TENANT_API_URL = `${API_BASE_URL}/xr50/trainingAssetRepository/tenants`;

  console.log('Computed URLs:');
  console.log(`  Tenants:       ${TENANT_API_URL}`);
  console.log(`  Materials:     ${API_BASE_URL}/api/${testTenant}/materials`);
  console.log(`  Assets:        ${API_BASE_URL}/api/${testTenant}/assets`);
  console.log(`  Programs:      ${API_BASE_URL}/api/${testTenant}/programs`);
  console.log(`  Users:         ${API_BASE_URL}/api/${testTenant}/users`);
  console.log('');

  // Quick connectivity check
  try {
    const response = await axios.get(`${API_BASE_URL}/health`, {
      timeout: 5000,
      validateStatus: () => true
    });

    if (response.status === 200) {
      console.log('API connectivity: OK');
    } else {
      console.warn(`API connectivity: Warning (status ${response.status})`);
    }
  } catch (error) {
    console.error(`API connectivity: FAILED - ${error.message}`);
    console.error('\nMake sure the API is running and accessible.');
    console.error(`Tried to reach: ${API_BASE_URL}/health\n`);
    throw new Error('Cannot reach API - aborting tests');
  }

  // Try Keycloak connectivity (skip if NO_AUTH mode)
  if (NO_AUTH) {
    console.log('Keycloak connectivity: Skipped (NO_AUTH mode)');
  } else {
    try {
      const response = await axios.get(`${KEYCLOAK_URL}/realms/${KEYCLOAK_REALM}/.well-known/openid-configuration`, {
        timeout: 5000,
        validateStatus: () => true
      });

      if (response.status === 200) {
        console.log('Keycloak connectivity: OK');
      } else {
        console.warn(`Keycloak connectivity: Warning (status ${response.status})`);
        console.warn('  Authentication tests may fail.\n');
      }
    } catch (error) {
      console.warn(`Keycloak connectivity: Not available - ${error.message}`);
      console.warn('  Authentication tests will be skipped.\n');
    }
  }

  // Create test tenant (unless using existing)
  if (!EXISTING_TENANT) {
    console.log(`\nCreating test tenant: ${testTenant}...`);

    try {
      const tenantData = {
        tenantName: testTenant,
        tenantGroup: 'functional-tests',
        description: `Functional test tenant created at ${new Date().toISOString()}`,
        storageType: 'S3',
        s3Config: {
          bucketName: S3_BUCKET,
          bucketRegion: S3_REGION,
          ...(S3_ENDPOINT && { endpoint: S3_ENDPOINT })
        },
        owner: {
          userName: 'testadmin',
          fullName: 'Test Administrator',
          userEmail: `admin@${testTenant}.test`,
          password: 'TestPass123!',
          admin: true
        }
      };

      const response = await axios.post(TENANT_API_URL, tenantData, {
        timeout: 30000,
        validateStatus: () => true,
        headers: { 'Content-Type': 'application/json' }
      });

      if (response.status === 200 || response.status === 201) {
        console.log(`Tenant created: ${testTenant}`);
        state.createdResources.tenants.push(testTenant);
        fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
      } else {
        console.error(`Failed to create tenant: ${response.status}`);
        console.error('Response:', JSON.stringify(response.data, null, 2));
        throw new Error(`Tenant creation failed: ${response.status}`);
      }
    } catch (error) {
      console.error(`Tenant creation error: ${error.message}`);
      if (error.response) {
        console.error('Response:', JSON.stringify(error.response.data, null, 2));
      }
      throw error;
    }
  } else {
    console.log(`\nUsing existing tenant: ${EXISTING_TENANT}`);
  }

  console.log('\nStarting tests...\n');
};
