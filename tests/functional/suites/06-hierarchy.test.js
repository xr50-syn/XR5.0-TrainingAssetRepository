const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Material Hierarchy Tests
 *
 * Verifies parent-child relationships and circular reference prevention.
 *
 * The three materials are created in beforeAll with hard assertions: if any create fails,
 * every test fails, rather than each one returning early and counting as a pass.
 */

describe('Material Hierarchy', () => {
  let parentMaterialId;
  let childMaterialId;
  let grandchildMaterialId;

  const createTracked = async (material) => {
    const response = await apiClient.createMaterial(material);
    if (![200, 201].includes(response.status)) {
      apiClient.logResponse(response, 'CREATE HIERARCHY MATERIAL');
    }
    expect([200, 201]).toContain(response.status);
    return apiClient.track('materials', response.data.id);
  };

  beforeAll(async () => {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);

    parentMaterialId = await createTracked(testData.createCompositeMaterial('parent'));
    childMaterialId = await createTracked(testData.createSimpleMaterial('child'));
    grandchildMaterialId = await createTracked(testData.createSimpleMaterial('grandchild'));
  });

  afterAll(async () => {
    expect(await apiClient.cleanupTracked()).toEqual([]);
  });

  describe('Assign Relationships', () => {
    test('can assign child to parent', async () => {
      const response = await apiClient.assignMaterialChild(
        parentMaterialId,
        childMaterialId
      );

      expect([200, 201, 204]).toContain(response.status);
    });

    test('can create multi-level hierarchy', async () => {
      const response = await apiClient.assignMaterialChild(
        childMaterialId,
        grandchildMaterialId
      );

      expect([200, 201, 204]).toContain(response.status);
    });
  });

  describe('Query Relationships', () => {
    test('can get children of parent', async () => {
      const response = await apiClient.getMaterialChildren(parentMaterialId);

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);

      const childIds = response.data.map(m => m.id);
      expect(childIds).toContain(childMaterialId);
    });

    test('can get parents of child', async () => {
      const response = await apiClient.getMaterialParents(childMaterialId);

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);

      const parentIds = response.data.map(m => m.id);
      expect(parentIds).toContain(parentMaterialId);
    });

    test('can get full hierarchy', async () => {
      const response = await apiClient.get(
        `${config.MATERIALS_API_URL}/${parentMaterialId}/hierarchy`
      );

      expect(response.status).toBe(200);
    });
  });

  describe('Circular Reference Prevention', () => {
    test('rejects direct circular reference (A -> A)', async () => {
      const response = await apiClient.assignMaterialChild(
        parentMaterialId,
        parentMaterialId
      );

      // Should reject with 400 Bad Request or similar
      expect([400, 409, 422]).toContain(response.status);
    });

    test('rejects indirect circular reference (A -> B -> A)', async () => {
      // Child is already a child of parent
      // Try to make parent a child of child (would create cycle)
      const response = await apiClient.assignMaterialChild(
        childMaterialId,
        parentMaterialId
      );

      // Should reject with 400 Bad Request or similar
      expect([400, 409, 422]).toContain(response.status);
    });

    test('rejects deep circular reference (A -> B -> C -> A)', async () => {
      // Grandchild is child of child, which is child of parent
      // Try to make parent a child of grandchild (would create cycle)
      const response = await apiClient.assignMaterialChild(
        grandchildMaterialId,
        parentMaterialId
      );

      // Should reject with 400 Bad Request or similar
      expect([400, 409, 422]).toContain(response.status);
    });
  });

  describe('Relationship Validation', () => {
    test('rejects assignment to non-existent parent', async () => {
      const response = await apiClient.assignMaterialChild(999999, childMaterialId);

      expect([400, 404]).toContain(response.status);
    });

    test('rejects assignment of non-existent child', async () => {
      const response = await apiClient.assignMaterialChild(parentMaterialId, 999999);

      expect([400, 404]).toContain(response.status);
    });
  });
});
