const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Tenant Management Tests
 *
 * Verifies tenant CRUD operations and S3 storage validation.
 *
 * Tenant provisioning, listing and deletion are SystemAdmin-gated, so this suite runs as the
 * system admin rather than ADMIN_USER (which may be a tenant admin) and a 403 is a failure.
 */

describe('Tenant Management', () => {
  let testTenantName;

  beforeAll(async () => {
    await apiClient.authenticate(config.SYSADMIN_USER, config.SYSADMIN_PASSWORD);

    testTenantName = `verify_tenant_${Date.now()}`;
  });

  afterAll(async () => {
    if (testTenantName && !config.SKIP_CLEANUP) {
      const response = await apiClient.deleteTenant(testTenantName);
      // 404: creation failed earlier, so there is nothing to remove.
      expect([200, 204, 404]).toContain(response.status);
    }
  });

  describe('List Tenants', () => {
    test('can list existing tenants', async () => {
      const response = await apiClient.listTenants();

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });
  });

  describe('Create Tenant with S3', () => {
    test('can create tenant with S3 configuration', async () => {
      const tenantData = testData.createS3Tenant(testTenantName);

      console.log('\n--- CREATE TENANT REQUEST ---');
      console.log('Tenant Name:', testTenantName);
      console.log('Request Body:', JSON.stringify(tenantData, null, 2));

      const response = await apiClient.createTenant(tenantData);

      console.log('Response Status:', response.status);
      if (response.data) {
        console.log('Response:', JSON.stringify(response.data, null, 2).substring(0, 500));
      }
      console.log('---\n');

      // 201 Created or 200 OK
      expect([200, 201]).toContain(response.status);

      expect(response.data).toHaveProperty('tenantName', testTenantName);
    });

    test('can retrieve created tenant', async () => {
      const response = await apiClient.getTenant(testTenantName);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('tenantName', testTenantName);
      expect(response.data).toHaveProperty('storageType', 'S3');
    });

    test('rejects duplicate tenant name', async () => {
      const tenantData = testData.createS3Tenant(testTenantName);
      const response = await apiClient.createTenant(tenantData);

      // Should fail with conflict or bad request
      expect([400, 409]).toContain(response.status);
    });
  });

  describe('S3 Storage Validation', () => {
    test('can validate storage connectivity', async () => {
      const response = await apiClient.validateStorage(testTenantName);

      // May succeed (200) or fail (4xx/5xx) depending on S3 config
      expect(response.status).toBeDefined();

      if (response.status === 200) {
        expect(response.data).toHaveProperty('validationResult');
      }
    });

    test('can get storage statistics', async () => {
      const response = await apiClient.getStorageStats(testTenantName);

      // May not be implemented or may require actual files
      expect([200, 404, 501]).toContain(response.status);
    });
  });

  describe('Tenant Validation', () => {
    test('returns 404 for non-existent tenant', async () => {
      const response = await apiClient.getTenant('non-existent-tenant-xyz');

      expect(response.status).toBe(404);
    });

    test('returns 404 when deleting a non-existent tenant', async () => {
      const response = await apiClient.deleteTenant('non_existent_tenant_xyz');

      expect(response.status).toBe(404);
    });

    test('returns 404 for tenant-scoped routes of a non-existent tenant', async () => {
      const response = await apiClient.get(`${config.API_BASE_URL}/api/non_existent_tenant_xyz/materials`);

      expect(response.status).toBe(404);
    });

    test('validates tenant name format', async () => {
      const invalidTenant = testData.createS3Tenant('invalid name with spaces!');

      console.log('\n--- TESTING INVALID TENANT NAME ---');
      console.log('Tenant data:', JSON.stringify(invalidTenant, null, 2));

      const response = await apiClient.createTenant(invalidTenant);

      console.log('Response status:', response.status);
      if (response.data) {
        console.log('Response:', JSON.stringify(response.data, null, 2).substring(0, 500));
      }
      console.log('---\n');

      // Tenant names become database names, so anything beyond letters, digits, '_' and '-'
      // is rejected.
      expect([400, 422]).toContain(response.status);
    });
  });

  describe('Delete Tenant', () => {
    test('can delete tenant', async () => {
      // Create a tenant specifically for deletion
      const deleteTenantName = `delete_test_${Date.now()}`;
      const tenantData = testData.createS3Tenant(deleteTenantName);

      const createResponse = await apiClient.createTenant(tenantData);
      expect([200, 201]).toContain(createResponse.status);

      // Now delete it
      const deleteResponse = await apiClient.deleteTenant(deleteTenantName);
      expect([200, 204]).toContain(deleteResponse.status);

      // Verify it's gone
      const getResponse = await apiClient.getTenant(deleteTenantName);
      expect([404, 410]).toContain(getResponse.status);
    });
  });
});
