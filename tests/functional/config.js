/**
 * Environment Configuration for Functional Tests
 *
 * All settings can be overridden via environment variables.
 *
 * Usage:
 *   API_URL=https://api.example.com npm test
 */

const fs = require('fs');
const path = require('path');

// Shared state file to ensure all test files use the same tenant
const STATE_FILE = path.join(__dirname, '.test-state.json');

/**
 * Get the test tenant name. Priority:
 * 1. EXISTING_TENANT env var (use pre-existing tenant)
 * 2. TEST_TENANT env var (explicit tenant name)
 * 3. Shared state file (created by setup.js)
 * 4. Generate new name (fallback)
 */
function getTestTenant() {
  // If using existing tenant, return that
  if (process.env.EXISTING_TENANT) {
    return process.env.EXISTING_TENANT;
  }

  // If explicit TEST_TENANT set, use that
  if (process.env.TEST_TENANT) {
    return process.env.TEST_TENANT;
  }

  // Try to read from shared state file (created by setup.js)
  try {
    if (fs.existsSync(STATE_FILE)) {
      const state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
      if (state.testTenant) {
        return state.testTenant;
      }
    }
  } catch (e) {
    // Ignore read errors
  }

  // Fallback: generate new name (should only happen in setup.js)
  return `test-${Date.now()}`;
}

const config = {
  // API Configuration
  API_BASE_URL: process.env.API_URL || 'http://localhost:5286',

  // Keycloak Authentication
  KEYCLOAK_URL: process.env.KEYCLOAK_URL || 'http://localhost:8180',
  KEYCLOAK_REALM: process.env.KEYCLOAK_REALM || 'xr50',
  KEYCLOAK_CLIENT_ID: process.env.KEYCLOAK_CLIENT || 'xr50-training-app',
  KEYCLOAK_CLIENT_SECRET: process.env.KEYCLOAK_CLIENT_SECRET || '',

  // Test User Credentials
  TEST_USER: process.env.TEST_USER || 'testuser',
  TEST_PASSWORD: process.env.TEST_PASSWORD || 'testuser123',

  // Admin credentials for tenant operations
  ADMIN_USER: process.env.ADMIN_USER || 'admin',
  ADMIN_PASSWORD: process.env.ADMIN_PASSWORD || 'admin123',

  // S3 Configuration for test tenant
  S3_BUCKET: process.env.S3_BUCKET || 'xr50-test-verification',
  S3_REGION: process.env.S3_REGION || 'eu-west-1',
  S3_ENDPOINT: process.env.S3_ENDPOINT || '', // Leave empty for AWS, set for MinIO

  // Existing tenant for material/asset tests (if you don't want to create new)
  EXISTING_TENANT: process.env.EXISTING_TENANT || '',

  // Timeouts
  REQUEST_TIMEOUT: parseInt(process.env.REQUEST_TIMEOUT) || 10000,

  // Debug mode
  DEBUG: process.env.DEBUG === 'true',

  // Skip cleanup (useful for debugging failed tests)
  SKIP_CLEANUP: process.env.SKIP_CLEANUP === 'true',

  // No authentication mode (for testing without Keycloak)
  NO_AUTH: process.env.NO_AUTH === 'true',

  // State file path (for setup/teardown coordination)
  STATE_FILE,

  // Test Tenant - computed once per config load
  get TEST_TENANT() {
    // Cache the value to ensure consistency within a single test file
    if (!this._testTenant) {
      this._testTenant = getTestTenant();
    }
    return this._testTenant;
  },

  // Computed values
  get KEYCLOAK_TOKEN_URL() {
    return `${this.KEYCLOAK_URL}/realms/${this.KEYCLOAK_REALM}/protocol/openid-connect/token`;
  },

  get TENANT_API_URL() {
    return `${this.API_BASE_URL}/xr50/trainingAssetRepository/tenants`;
  },

  get MATERIALS_API_URL() {
    const tenant = this.EXISTING_TENANT || this.TEST_TENANT;
    return `${this.API_BASE_URL}/api/${tenant}/materials`;
  },

  get ASSETS_API_URL() {
    const tenant = this.EXISTING_TENANT || this.TEST_TENANT;
    return `${this.API_BASE_URL}/api/${tenant}/assets`;
  },

  get PROGRAMS_API_URL() {
    const tenant = this.EXISTING_TENANT || this.TEST_TENANT;
    return `${this.API_BASE_URL}/api/${tenant}/programs`;
  },

  get USERS_API_URL() {
    const tenant = this.EXISTING_TENANT || this.TEST_TENANT;
    return `${this.API_BASE_URL}/api/${tenant}/users`;
  },

  /**
   * Get the effective tenant name (for API calls)
   */
  getEffectiveTenant() {
    return this.EXISTING_TENANT || this.TEST_TENANT;
  }
};

module.exports = config;
