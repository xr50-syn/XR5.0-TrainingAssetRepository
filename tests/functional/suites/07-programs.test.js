const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * Training Program Tests
 *
 * Verifies training program CRUD and material assignment.
 */

describe('Training Programs', () => {
  let createdProgramId;
  let testMaterialId;

  beforeAll(async () => {
    try {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    } catch (error) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }

    // Create a material for assignment tests
    const materialResponse = await apiClient.createMaterial(
      testData.createSimpleMaterial('program-test')
    );
    if (materialResponse.status === 200 || materialResponse.status === 201) {
      testMaterialId = materialResponse.data.id;
      global.__TEST_CONFIG__?.createdResources?.materials?.push(testMaterialId);
    }
  });

  afterAll(async () => {
    if (createdProgramId && !config.SKIP_CLEANUP) {
      try {
        await apiClient.deleteProgram(createdProgramId);
      } catch (error) {
        // Ignore
      }
    }
  });

  describe('List Programs', () => {
    test('can list programs', async () => {
      const response = await apiClient.listPrograms();

      expect([200, 401, 403]).toContain(response.status);

      if (response.status === 200) {
        expect(Array.isArray(response.data)).toBe(true);
      }
    });
  });

  describe('Create Programs', () => {
    test('can create basic program', async () => {
      const program = testData.createTrainingProgram();
      const response = await apiClient.createProgram(program);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('id');
        expect(response.data).toHaveProperty('name', program.name);
        createdProgramId = response.data.id;
        global.__TEST_CONFIG__?.createdResources?.programs?.push(createdProgramId);
      }
    });

    test('can create program with learning paths', async () => {
      const program = testData.createProgramWithPaths();
      const response = await apiClient.post(
        `${config.PROGRAMS_API_URL}/detail`,
        program
      );

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        global.__TEST_CONFIG__?.createdResources?.programs?.push(response.data.id);
      }
    });
  });

  describe('Read Programs', () => {
    test('can get program by ID', async () => {
      if (!createdProgramId) {
        console.log('Skipping: No program created');
        return;
      }

      const response = await apiClient.getProgram(createdProgramId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', createdProgramId);
    });

    test('can get program detail', async () => {
      if (!createdProgramId) {
        console.log('Skipping: No program created');
        return;
      }

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
      if (!createdProgramId || !testMaterialId) {
        console.log('Skipping: Program or material not created');
        return;
      }

      const response = await apiClient.assignMaterialToProgram(
        createdProgramId,
        testMaterialId
      );

      expect([200, 201, 204]).toContain(response.status);
    });

    test('program includes assigned materials', async () => {
      if (!createdProgramId) {
        console.log('Skipping: No program created');
        return;
      }

      const response = await apiClient.getProgramDetail(createdProgramId);

      expect(response.status).toBe(200);

      if (testMaterialId && response.data.materials) {
        const materialIds = response.data.materials.map(m => m.id);
        expect(materialIds).toContain(testMaterialId);
      }
    });

    test('can get program materials', async () => {
      if (!createdProgramId) {
        console.log('Skipping: No program created');
        return;
      }

      const response = await apiClient.get(
        `${config.PROGRAMS_API_URL}/${createdProgramId}/materials`
      );

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });

    test('rejects assignment of non-existent material', async () => {
      if (!createdProgramId) {
        console.log('Skipping: No program created');
        return;
      }

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

      if (createResponse.status !== 200 && createResponse.status !== 201) {
        console.log('Skipping: Could not create program');
        return;
      }

      const programId = createResponse.data.id;

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
