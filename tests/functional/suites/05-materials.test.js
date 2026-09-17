const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Material CRUD Tests
 *
 * Verifies material creation, retrieval, update, and deletion.
 *
 * The suite authenticates as ADMIN_USER, which must be allowed to author content, so a
 * 401/403/500 is a failure rather than an accepted outcome. Tests that depend on an earlier
 * create fail when it is missing instead of returning early: an early return counts as a pass,
 * which hid these paths behind a green run.
 */

describe('Material Operations', () => {
  let createdMaterialId;

  beforeAll(async () => {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
  });

  afterAll(async () => {
    expect(await apiClient.cleanupTracked()).toEqual([]);
  });

  describe('List Materials', () => {
    test('can list materials', async () => {
      const response = await apiClient.listMaterials();

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });
  });

  describe('Create Materials', () => {
    // Helper to log material creation failures
    const logMaterialFailure = (material, response, testName) => {
      if (response.status >= 400) {
        console.log(`\n--- ${testName} FAILED ---`);
        console.log('URL:', config.MATERIALS_API_URL);
        console.log('Request:', JSON.stringify(material, null, 2));
        apiClient.logResponse(response, testName);
        console.log('---\n');
      }
    };

    test('can create video material', async () => {
      const material = testData.createVideoMaterial();
      const response = await apiClient.createMaterial(material);

      logMaterialFailure(material, response, 'CREATE VIDEO');
      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('type', 'video');
      createdMaterialId = apiClient.track('materials', response.data.id);
    });

    test('can create checklist material', async () => {
      const material = testData.createChecklistMaterial();
      const response = await apiClient.createMaterial(material);

      logMaterialFailure(material, response, 'CREATE CHECKLIST');
      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('type', 'checklist');
      apiClient.track('materials', response.data.id);
    });

    test('can create workflow material', async () => {
      const material = testData.createWorkflowMaterial();
      const response = await apiClient.createMaterial(material);

      logMaterialFailure(material, response, 'CREATE WORKFLOW');
      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('type', 'workflow');
      apiClient.track('materials', response.data.id);
    });

    test('can create chatbot material', async () => {
      const material = testData.createChatbotMaterial();
      const response = await apiClient.createMaterial(material);

      logMaterialFailure(material, response, 'CREATE CHATBOT');
      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('type', 'chatbot');
      apiClient.track('materials', response.data.id);
    });
  });

  describe('Read Materials', () => {
    test('can get material by ID', async () => {
      expect(createdMaterialId).toBeDefined();

      const response = await apiClient.getMaterial(createdMaterialId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', createdMaterialId);
    });

    test('can get material detail', async () => {
      expect(createdMaterialId).toBeDefined();

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
    // PUT replaces the material, so every update sends the full object.
    test('can update material name', async () => {
      expect(createdMaterialId).toBeDefined();

      const newName = `Updated Material ${Date.now()}`;
      const response = await apiClient.updateMaterial(createdMaterialId, {
        ...testData.createVideoMaterial(),
        name: newName
      });

      expect([200, 204]).toContain(response.status);

      const getResponse = await apiClient.getMaterial(createdMaterialId);
      expect(getResponse.data.name).toBe(newName);
    });

    test('can update material description', async () => {
      expect(createdMaterialId).toBeDefined();

      const newDescription = 'Updated description for verification';
      const response = await apiClient.updateMaterial(createdMaterialId, {
        ...testData.createVideoMaterial(),
        description: newDescription
      });

      expect([200, 204]).toContain(response.status);

      const getResponse = await apiClient.getMaterial(createdMaterialId);
      expect(getResponse.data.description).toBe(newDescription);
    });

    test('rejects update without name and keeps the stored name', async () => {
      expect(createdMaterialId).toBeDefined();

      const before = await apiClient.getMaterial(createdMaterialId);
      const response = await apiClient.updateMaterial(createdMaterialId, {
        description: 'Description only',
        type: 'Video'
      });

      expect(response.status).toBe(400);

      const after = await apiClient.getMaterial(createdMaterialId);
      expect(after.data.name).toBe(before.data.name);
    });
  });

  describe('Delete Materials', () => {
    test('can delete material', async () => {
      // Create a material specifically for deletion
      const material = testData.createSimpleMaterial('delete-test');
      const createResponse = await apiClient.createMaterial(material);

      expect([200, 201]).toContain(createResponse.status);
      // Tracked so a failed delete below still gets cleaned up.
      const materialId = apiClient.track('materials', createResponse.data.id);

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
