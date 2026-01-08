const config = require('./config');

/**
 * Global test teardown
 *
 * Runs once after all tests to clean up created resources
 */

module.exports = async function globalTeardown() {
  console.log('\n========================================');
  console.log('  Cleanup');
  console.log('========================================\n');

  if (config.SKIP_CLEANUP) {
    console.log('Cleanup skipped (SKIP_CLEANUP=true)');
    console.log('Remember to manually clean up test resources.\n');
    return;
  }

  const resources = global.__TEST_CONFIG__?.createdResources;

  if (!resources) {
    console.log('No resources tracked for cleanup.\n');
    return;
  }

  const axios = require('axios');
  const apiClient = require('./helpers/api-client');

  // Try to authenticate for cleanup
  try {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
  } catch (error) {
    console.warn('Could not authenticate for cleanup. Some resources may remain.');
  }

  let cleanedCount = 0;
  let failedCount = 0;

  // Clean up in reverse order of dependency

  // 1. Delete users
  for (const userName of resources.users) {
    try {
      await apiClient.deleteUser(userName);
      console.log(`  Deleted user: ${userName}`);
      cleanedCount++;
    } catch (error) {
      console.warn(`  Failed to delete user ${userName}: ${error.message}`);
      failedCount++;
    }
  }

  // 2. Delete programs
  for (const programId of resources.programs) {
    try {
      await apiClient.deleteProgram(programId);
      console.log(`  Deleted program: ${programId}`);
      cleanedCount++;
    } catch (error) {
      console.warn(`  Failed to delete program ${programId}: ${error.message}`);
      failedCount++;
    }
  }

  // 3. Delete assets
  for (const assetId of resources.assets) {
    try {
      await apiClient.deleteAsset(assetId);
      console.log(`  Deleted asset: ${assetId}`);
      cleanedCount++;
    } catch (error) {
      console.warn(`  Failed to delete asset ${assetId}: ${error.message}`);
      failedCount++;
    }
  }

  // 4. Delete materials
  for (const materialId of resources.materials) {
    try {
      await apiClient.deleteMaterial(materialId);
      console.log(`  Deleted material: ${materialId}`);
      cleanedCount++;
    } catch (error) {
      console.warn(`  Failed to delete material ${materialId}: ${error.message}`);
      failedCount++;
    }
  }

  // 5. Delete tenants (last, as other resources depend on them)
  for (const tenantName of resources.tenants) {
    try {
      await apiClient.deleteTenant(tenantName);
      console.log(`  Deleted tenant: ${tenantName}`);
      cleanedCount++;
    } catch (error) {
      console.warn(`  Failed to delete tenant ${tenantName}: ${error.message}`);
      failedCount++;
    }
  }

  console.log('');
  console.log(`Cleanup complete: ${cleanedCount} resources deleted, ${failedCount} failed.`);
  console.log('');
};
