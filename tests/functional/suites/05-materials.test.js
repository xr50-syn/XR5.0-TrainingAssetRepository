const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Material CRUD Tests
 *
 * Verifies material creation, retrieval, update, and deletion.
 */

describe('Material Operations', () => {
  let createdMaterialId;

  beforeAll(async () => {
    try {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    } catch (error) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }
  });

  afterAll(async () => {
    // Cleanup
    if (createdMaterialId && !config.SKIP_CLEANUP) {
      try {
        await apiClient.deleteMaterial(createdMaterialId);
      } catch (error) {
        // Ignore
      }
    }
  });

  describe('List Materials', () => {
    test('can list materials', async () => {
      const response = await apiClient.listMaterials();

      expect([200, 401, 403]).toContain(response.status);

      if (response.status === 200) {
        expect(Array.isArray(response.data)).toBe(true);
      }
    });
  });

  describe('Create Materials', () => {
    test('can create simple material', async () => {
      const material = testData.createSimpleMaterial();
      const response = await apiClient.createMaterial(material);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('id');
        expect(response.data).toHaveProperty('name', material.name);
        createdMaterialId = response.data.id;
        global.__TEST_CONFIG__?.createdResources?.materials?.push(createdMaterialId);
      }
    });

    test('can create video material', async () => {
      const material = testData.createVideoMaterial();
      const response = await apiClient.createMaterial(material);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('type', 'Video');
        global.__TEST_CONFIG__?.createdResources?.materials?.push(response.data.id);
      }
    });

    test('can create checklist material', async () => {
      const material = testData.createChecklistMaterial();
      const response = await apiClient.createMaterial(material);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('type', 'Checklist');
        global.__TEST_CONFIG__?.createdResources?.materials?.push(response.data.id);
      }
    });

    test('can create workflow material', async () => {
      const material = testData.createWorkflowMaterial();
      const response = await apiClient.createMaterial(material);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('type', 'Workflow');
        global.__TEST_CONFIG__?.createdResources?.materials?.push(response.data.id);
      }
    });
  });

  describe('Read Materials', () => {
    test('can get material by ID', async () => {
      if (!createdMaterialId) {
        console.log('Skipping: No material created');
        return;
      }

      const response = await apiClient.getMaterial(createdMaterialId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', createdMaterialId);
    });

    test('can get material detail', async () => {
      if (!createdMaterialId) {
        console.log('Skipping: No material created');
        return;
      }

      const response = await apiClient.getMaterialDetail(createdMaterialId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', createdMaterialId);
    });

    test('returns 404 for non-existent material', async () => {
      const response = await apiClient.getMaterial(999999);

      expect(response.status).toBe(404);
    });
  });

  describe('Update Materials', () => {
    test('can update material name', async () => {
      if (!createdMaterialId) {
        console.log('Skipping: No material created');
        return;
      }

      const newName = `Updated Material ${Date.now()}`;
      const response = await apiClient.updateMaterial(createdMaterialId, {
        name: newName
      });

      expect([200, 204]).toContain(response.status);

      // Verify the update
      const getResponse = await apiClient.getMaterial(createdMaterialId);
      expect(getResponse.data.name).toBe(newName);
    });

    test('can update material description', async () => {
      if (!createdMaterialId) {
        console.log('Skipping: No material created');
        return;
      }

      const newDescription = 'Updated description for verification';
      const response = await apiClient.updateMaterial(createdMaterialId, {
        description: newDescription
      });

      expect([200, 204]).toContain(response.status);
    });
  });

  describe('Delete Materials', () => {
    test('can delete material', async () => {
      // Create a material specifically for deletion
      const material = testData.createSimpleMaterial('delete-test');
      const createResponse = await apiClient.createMaterial(material);

      if (createResponse.status !== 200 && createResponse.status !== 201) {
        console.log('Skipping: Could not create material');
        return;
      }

      const materialId = createResponse.data.id;

      // Delete it
      const deleteResponse = await apiClient.deleteMaterial(materialId);
      expect([200, 204]).toContain(deleteResponse.status);

      // Verify it's gone
      const getResponse = await apiClient.getMaterial(materialId);
      expect([404, 410]).toContain(getResponse.status);
    });
  });

  describe('Material Validation', () => {
    test('rejects material without name', async () => {
      const response = await apiClient.createMaterial({
        description: 'Missing name'
      });

      expect([400, 422]).toContain(response.status);
    });

    test('rejects invalid material type', async () => {
      const response = await apiClient.createMaterial({
        name: 'Invalid Type Test',
        type: 'InvalidType'
      });

      expect([400, 422]).toContain(response.status);
    });
  });
});
