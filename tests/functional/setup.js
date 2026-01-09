const config = require('./config');

/**
 * Global test setup
 *
 * Runs once before all tests to:
 * 1. Verify API is reachable
 * 2. Authenticate with Keycloak
 * 3. Store auth token for tests
 */

module.exports = async function globalSetup() {
  console.log('\n========================================');
  console.log('  XR5.0 Functional Test Suite');
  console.log('========================================\n');

  console.log('Configuration:');
  console.log(`  API URL:      ${config.API_BASE_URL}`);
  console.log(`  Keycloak:     ${config.KEYCLOAK_URL}`);
  console.log(`  Test Tenant:  ${config.TEST_TENANT}`);
  console.log(`  S3 Bucket:    ${config.S3_BUCKET}`);
  console.log(`  S3 Region:    ${config.S3_REGION}`);
  if (config.EXISTING_TENANT) {
    console.log(`  Using Tenant: ${config.EXISTING_TENANT}`);
  }
  console.log('');

  // Store config in global for tests to access
  global.__TEST_CONFIG__ = {
    apiUrl: config.API_BASE_URL,
    testTenant: config.TEST_TENANT,
    existingTenant: config.EXISTING_TENANT,
    createdResources: {
      tenants: [],
      materials: [],
      assets: [],
      programs: [],
      users: []
    }
  };

  // Quick connectivity check
  try {
    const axios = require('axios');
    const response = await axios.get(`${config.API_BASE_URL}/health`, {
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
    console.error(`Tried to reach: ${config.API_BASE_URL}/health\n`);
    throw new Error('Cannot reach API - aborting tests');
  }

  // Try Keycloak connectivity (don't fail if unavailable)
  try {
    const axios = require('axios');
    const response = await axios.get(`${config.KEYCLOAK_URL}/realms/${config.KEYCLOAK_REALM}/.well-known/openid-configuration`, {
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
    global.__TEST_CONFIG__.keycloakAvailable = false;
  }

  console.log('\nStarting tests...\n');
};
