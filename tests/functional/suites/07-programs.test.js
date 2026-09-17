const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Training Program Tests
 *
 * Verifies training program CRUD and material assignment.
 *
 * The suite authenticates as ADMIN_USER, which must be allowed to author content, so a
 * 401/403 is a failure. Tests that depend on an earlier create fail when it is missing instead
 * of returning early and counting as a pass.
 */

describe('Training Programs', () => {
  let createdProgramId;
  let testMaterialId;

  beforeAll(async () => {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);

    // Create a material for assignment tests
    const materialResponse = await apiClient.createMaterial(
      testData.createSimpleMaterial('program-test')
    );
    expect([200, 201]).toContain(materialResponse.status);
    testMaterialId = apiClient.track('materials', materialResponse.data.id);
  });

  afterAll(async () => {
    expect(await apiClient.cleanupTracked()).toEqual([]);
  });

  describe('List Programs', () => {
    test('can list programs', async () => {
      const response = await apiClient.listPrograms();

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });
  });

  describe('Create Programs', () => {
    test('can create basic program', async () => {
      const program = testData.createTrainingProgram();
      const response = await apiClient.createProgram(program);

      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('id');
      expect(response.data).toHaveProperty('name', program.name);
      createdProgramId = apiClient.track('programs', response.data.id);
    });

    test('can create program with learning paths', async () => {
      const program = testData.createProgramWithPaths();
      const response = await apiClient.post(
        `${config.PROGRAMS_API_URL}/detail`,
        program
      );

      expect([200, 201]).toContain(response.status);
      apiClient.track('programs', response.data.id);
    });
  });

  describe('Read Programs', () => {
    test('can get program by ID', async () => {
      expect(createdProgramId).toBeDefined();

      const response = await apiClient.getProgram(createdProgramId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', createdProgramId);
    });

    test('can get program detail', async () => {
      expect(createdProgramId).toBeDefined();

      const response = await apiClient.getProgramDetail(createdProgramId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', createdProgramId);
    });

    test('returns 404 for non-existent program', async () => {
      const response = await apiClient.getProgram(999999);

      expect(response.status).toBe(404);
    });
  });

  describe('Material Assignment', () => {
    test('can assign material to program', async () => {
      expect(createdProgramId).toBeDefined();

      const response = await apiClient.assignMaterialToProgram(
        createdProgramId,
        testMaterialId
      );

      expect([200, 201, 204]).toContain(response.status);
    });

    test('program includes assigned materials', async () => {
      expect(createdProgramId).toBeDefined();

      const response = await apiClient.getProgramDetail(createdProgramId);

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data.materials)).toBe(true);
      const materialIds = response.data.materials.map(m => m.id);
      expect(materialIds).toContain(testMaterialId);
    });

    test('can get program materials', async () => {
      expect(createdProgramId).toBeDefined();

      const response = await apiClient.get(
        `${config.PROGRAMS_API_URL}/${createdProgramId}/materials`
      );

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });

    test('rejects assignment of non-existent material', async () => {
      expect(createdProgramId).toBeDefined();

      const response = await apiClient.assignMaterialToProgram(
        createdProgramId,
        999999
      );

      expect([400, 404]).toContain(response.status);
    });
  });

  describe('Delete Programs', () => {
    test('can delete program', async () => {
      // Create a program specifically for deletion
      const program = testData.createTrainingProgram('delete-test');
      const createResponse = await apiClient.createProgram(program);

      expect([200, 201]).toContain(createResponse.status);
      // Tracked so a failed delete below still gets cleaned up.
      const programId = apiClient.track('programs', createResponse.data.id);

      // Delete it
      const deleteResponse = await apiClient.deleteProgram(programId);
      expect([200, 204]).toContain(deleteResponse.status);

      // Verify it's gone
      const getResponse = await apiClient.getProgram(programId);
      expect([404, 410]).toContain(getResponse.status);
    });
  });

  describe('Program Validation', () => {
    test('rejects program without name', async () => {
      const response = await apiClient.createProgram({
        description: 'Missing name'
      });

      expect([400, 422]).toContain(response.status);
    });
  });
});
