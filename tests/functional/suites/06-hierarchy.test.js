const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Material Hierarchy Tests
 *
 * Verifies parent-child relationships and circular reference prevention.
 */

describe('Material Hierarchy', () => {
  let parentMaterialId;
  let childMaterialId;
  let grandchildMaterialId;

  beforeAll(async () => {
    try {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    } catch (error) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }

    // Create parent material
    const parentResponse = await apiClient.createMaterial(
      testData.createCompositeMaterial('parent')
    );
    if (parentResponse.status === 200 || parentResponse.status === 201) {
      parentMaterialId = parentResponse.data.id;
      global.__TEST_CONFIG__?.createdResources?.materials?.push(parentMaterialId);
    }

    // Create child material
    const childResponse = await apiClient.createMaterial(
      testData.createSimpleMaterial('child')
    );
    if (childResponse.status === 200 || childResponse.status === 201) {
      childMaterialId = childResponse.data.id;
      global.__TEST_CONFIG__?.createdResources?.materials?.push(childMaterialId);
    }

    // Create grandchild material
    const grandchildResponse = await apiClient.createMaterial(
      testData.createSimpleMaterial('grandchild')
    );
    if (grandchildResponse.status === 200 || grandchildResponse.status === 201) {
      grandchildMaterialId = grandchildResponse.data.id;
      global.__TEST_CONFIG__?.createdResources?.materials?.push(grandchildMaterialId);
    }
  });

  describe('Assign Relationships', () => {
    test('can assign child to parent', async () => {
      if (!parentMaterialId || !childMaterialId) {
        console.log('Skipping: Materials not created');
        return;
      }

      const response = await apiClient.assignMaterialChild(
        parentMaterialId,
        childMaterialId
      );

      expect([200, 201, 204]).toContain(response.status);
    });

    test('can create multi-level hierarchy', async () => {
      if (!childMaterialId || !grandchildMaterialId) {
        console.log('Skipping: Materials not created');
        return;
      }

      const response = await apiClient.assignMaterialChild(
        childMaterialId,
        grandchildMaterialId
      );

      expect([200, 201, 204]).toContain(response.status);
    });
  });

  describe('Query Relationships', () => {
    test('can get children of parent', async () => {
      if (!parentMaterialId) {
        console.log('Skipping: Parent material not created');
        return;
      }

      const response = await apiClient.getMaterialChildren(parentMaterialId);

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);

      if (childMaterialId) {
        const childIds = response.data.map(m => m.id);
        expect(childIds).toContain(childMaterialId);
      }
    });

    test('can get parents of child', async () => {
      if (!childMaterialId) {
        console.log('Skipping: Child material not created');
        return;
      }

      const response = await apiClient.getMaterialParents(childMaterialId);

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);

      if (parentMaterialId) {
        const parentIds = response.data.map(m => m.id);
        expect(parentIds).toContain(parentMaterialId);
      }
    });

    test('can get full hierarchy', async () => {
      if (!parentMaterialId) {
        console.log('Skipping: Parent material not created');
        return;
      }

      const response = await apiClient.get(
        `${config.MATERIALS_API_URL}/${parentMaterialId}/hierarchy`
      );

      expect(response.status).toBe(200);
    });
  });

  describe('Circular Reference Prevention', () => {
    test('rejects direct circular reference (A -> A)', async () => {
      if (!parentMaterialId) {
        console.log('Skipping: Parent material not created');
        return;
      }

      const response = await apiClient.assignMaterialChild(
        parentMaterialId,
        parentMaterialId
      );

      // Should reject with 400 Bad Request or similar
      expect([400, 409, 422]).toContain(response.status);
    });

    test('rejects indirect circular reference (A -> B -> A)', async () => {
      if (!parentMaterialId || !childMaterialId) {
        console.log('Skipping: Materials not created');
        return;
      }

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
      if (!parentMaterialId || !grandchildMaterialId) {
        console.log('Skipping: Materials not created');
        return;
      }

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
      if (!childMaterialId) {
        console.log('Skipping: Child material not created');
        return;
      }

      const response = await apiClient.assignMaterialChild(999999, childMaterialId);

      expect([400, 404]).toContain(response.status);
    });

    test('rejects assignment of non-existent child', async () => {
      if (!parentMaterialId) {
        console.log('Skipping: Parent material not created');
        return;
      }

      const response = await apiClient.assignMaterialChild(parentMaterialId, 999999);

      expect([400, 404]).toContain(response.status);
    });
  });
});
