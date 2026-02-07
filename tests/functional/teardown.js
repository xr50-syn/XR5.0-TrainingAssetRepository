const fs = require('fs');
const path = require('path');

/**
 * Global test teardown
 *
 * Runs once after all tests to:
 * 1. Clean up created resources
 * 2. Delete test tenant (if we created it)
 * 3. Remove state file
 */

module.exports = async function globalTeardown() {
  const config = require('./config');
  const STATE_FILE = path.join(__dirname, '.test-state.json');

  console.log('\n========================================');
  console.log('  Cleanup');
  console.log('========================================\n');

  if (config.SKIP_CLEANUP) {
    console.log('Cleanup skipped (SKIP_CLEANUP=true)');
    console.log('Remember to manually clean up test resources.\n');
    return;
  }

  // Read state from file
  let state = null;
  try {
    if (fs.existsSync(STATE_FILE)) {
      state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
    }
  } catch (error) {
    console.warn(`Could not read state file: ${error.message}`);
  }

  if (!state) {
    console.log('No state file found - nothing to clean up.\n');
    return;
  }

  const axios = require('axios');
  const API_BASE_URL = process.env.API_URL || 'http://localhost:5286';
  const TENANT_API_URL = `${API_BASE_URL}/xr50/trainingAssetRepository/tenants`;

  let cleanedCount = 0;
  let failedCount = 0;

  // Delete the test tenant if we created it
  if (state.createdTenant && state.testTenant && !state.existingTenant) {
    console.log(`Deleting test tenant: ${state.testTenant}...`);

    try {
      const response = await axios.delete(`${TENANT_API_URL}/${state.testTenant}`, {
        timeout: 30000,
        validateStatus: () => true
      });

      if (response.status === 200 || response.status === 204) {
        console.log(`  Deleted tenant: ${state.testTenant}`);
        cleanedCount++;
      } else if (response.status === 404 || response.status === 500) {
        // Tenant might already be deleted or not found
        console.log(`  Tenant already deleted or not found: ${state.testTenant}`);
      } else {
        console.warn(`  Failed to delete tenant: ${response.status}`);
        failedCount++;
      }
    } catch (error) {
      console.warn(`  Failed to delete tenant ${state.testTenant}: ${error.message}`);
      failedCount++;
    }
  } else if (state.existingTenant) {
    console.log(`Preserving existing tenant: ${state.existingTenant}`);
  }

  // Clean up state file
  try {
    if (fs.existsSync(STATE_FILE)) {
      fs.unlinkSync(STATE_FILE);
    }
  } catch (error) {
    console.warn(`Could not delete state file: ${error.message}`);
  }

  console.log('');
  console.log(`Cleanup complete: ${cleanedCount} resources deleted, ${failedCount} failed.`);
  console.log('');
};
