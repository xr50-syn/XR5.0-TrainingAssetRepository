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
    test('adding an image annotation preserves its checklist entry relationship', async () => {
      const imageName = `Related image ${Date.now()}`;
      const imageResponse = await apiClient.createMaterial({
        name: imageName,
        type: 'image',
        annotations: [{ id: 'first', text: 'First', x: 10, y: 20 }]
      });
      expect([200, 201]).toContain(imageResponse.status);
      const imageId = apiClient.track('materials', imageResponse.data.id);

      const checklistResponse = await apiClient.createMaterial({
        name: `Checklist with image ${Date.now()}`,
        type: 'checklist',
        entries: [{ text: 'Entry', related: [{ id: imageId }] }]
      });
      expect([200, 201]).toContain(checklistResponse.status);
      const checklistId = apiClient.track('materials', checklistResponse.data.id);

      const relatedIds = async () => {
        const detail = await apiClient.getMaterialDetail(checklistId);
        expect(detail.status).toBe(200);
        return detail.data.config.entries[0].related.map(material => String(material.id));
      };
      expect(await relatedIds()).toEqual([String(imageId)]);

      const originalImage = await apiClient.getMaterialDetail(imageId);
      expect(originalImage.status).toBe(200);
      const firstAnnotationId = originalImage.data.config.annotations[0].id;

      const update = await apiClient.updateMaterial(imageId, {
        name: imageName,
        type: 'image',
        annotations: [
          { imageAnnotationId: firstAnnotationId, clientId: 'first', text: 'First', x: 10, y: 20 },
          { id: 'second', text: 'Second', x: 30, y: 40 }
        ]
      });
      expect([200, 204]).toContain(update.status);

      const imageDetail = await apiClient.getMaterialDetail(imageId);
      expect(imageDetail.status).toBe(200);
      expect(imageDetail.data.config.annotations).toHaveLength(2);
      expect(imageDetail.data.config.annotations.map(annotation => annotation.id))
        .toContain(firstAnnotationId);
      expect(await relatedIds()).toEqual([String(imageId)]);
    });

    const relatedTargetCases = [
      {
        type: 'video', childKey: 'timestamps',
        initial: { timestamps: [{ title: 'First', startTime: '0' }] },
        updated: { timestamps: [{ title: 'First', startTime: '0' }, { title: 'Second', startTime: '10' }] }
      },
      {
        type: 'checklist', childKey: 'entries',
        initial: { entries: [{ text: 'First' }] },
        updated: { entries: [{ text: 'First' }, { text: 'Second' }] }
      },
      {
        type: 'workflow', childKey: 'steps',
        initial: { steps: [{ title: 'First', content: 'Start' }] },
        updated: { steps: [{ title: 'First', content: 'Start' }, { title: 'Second', content: 'Continue' }] }
      },
      {
        type: 'questionnaire', childKey: 'entries',
        initial: { entries: [{ text: 'First' }] },
        updated: { entries: [{ text: 'First' }, { text: 'Second' }] }
      },
      {
        type: 'quiz', childKey: 'questions',
        initial: { questions: [{ text: 'First', questionType: 'text', answers: [{ text: 'One' }] }] },
        updated: { questions: [
          { text: 'First', questionType: 'text', answers: [{ text: 'One' }] },
          { text: 'Second', questionType: 'text', answers: [{ text: 'Two' }] }
        ] }
      },
      { type: 'pdf', initial: { pdfPath: '/test.pdf' }, updated: { pdfPath: '/updated.pdf' } },
      { type: 'unity', initial: { unityVersion: '1' }, updated: { unityVersion: '2' } },
      { type: 'chatbot', initial: { chatbotPrompt: 'First' }, updated: { chatbotPrompt: 'Second' } },
      { type: 'ai_assistant', initial: {}, updated: {} },
      { type: 'innov_chatbot', initial: { expertiseLevel: 'beginner' }, updated: { expertiseLevel: 'expert' } },
      { type: 'mqtt_template', initial: { message_text: 'First' }, updated: { message_text: 'Second' } },
      { type: 'default', initial: {}, updated: {} }
    ];

    test.each(relatedTargetCases)('updating a $type material preserves incoming checklist links', async ({
      type, childKey, initial, updated
    }) => {
      const name = `Related ${type} ${Date.now()}`;
      const created = await apiClient.createMaterial({ name, type, ...initial });
      expect([200, 201]).toContain(created.status);
      const targetId = apiClient.track('materials', created.data.id);

      const checklist = await apiClient.createMaterial({
        name: `Checklist for ${type} ${Date.now()}`,
        type: 'checklist',
        entries: [{ text: 'Related target', related: [{ id: targetId }] }]
      });
      expect([200, 201]).toContain(checklist.status);
      const checklistId = apiClient.track('materials', checklist.data.id);

      const relatedIds = async () => {
        const detail = await apiClient.getMaterialDetail(checklistId);
        expect(detail.status).toBe(200);
        return detail.data.config.entries[0].related.map(material => String(material.id));
      };
      expect(await relatedIds()).toEqual([String(targetId)]);

      const response = await apiClient.updateMaterial(targetId, {
        name, type, description: 'Updated target', ...updated
      });
      expect([200, 204]).toContain(response.status);

      const detail = await apiClient.getMaterialDetail(targetId);
      expect(detail.status).toBe(200);
      expect(detail.data.description).toBe('Updated target');
      if (childKey) expect(detail.data.config[childKey]).toHaveLength(2);
      expect(await relatedIds()).toEqual([String(targetId)]);
    });

    const relatedSourceCases = [
      {
        source: 'workflow step', type: 'workflow',
        data: id => ({ steps: [{ title: 'Step', content: 'Do it', related: [{ id }] }] }),
        related: detail => detail.config.steps[0].related
      },
      {
        source: 'video timestamp', type: 'video',
        data: id => ({ timestamps: [{ title: 'Start', startTime: '0', related: [{ id }] }] }),
        related: detail => detail.config.timestamps[0].related
      },
      {
        source: 'questionnaire entry', type: 'questionnaire',
        data: id => ({ entries: [{ text: 'Question', related: [{ id }] }] }),
        related: detail => detail.config.entries[0].related
      },
      {
        source: 'quiz question', type: 'quiz',
        data: id => ({ questions: [{ text: 'Question', questionType: 'text', related: [{ id }] }] }),
        related: detail => detail.config.questions[0].related
      },
      {
        source: 'quiz answer', type: 'quiz',
        data: id => ({ questions: [{ text: 'Question', questionType: 'text', answers: [
          { text: 'Answer', related: [{ id }] }
        ] }] }),
        related: detail => detail.config.questions[0].answers[0].related
      },
      {
        source: 'image annotation', type: 'image',
        data: id => ({ annotations: [{ id: 'first', text: 'Note', x: 1, y: 2, related: [{ id }] }] }),
        related: detail => detail.config.annotations[0].related
      }
    ];

    test.each(relatedSourceCases)('$source retains its related material after that material changes', async ({
      source, type, data, related
    }) => {
      const targetName = `Target for ${source} ${Date.now()}`;
      const target = await apiClient.createMaterial({
        name: targetName, type: 'default'
      });
      expect([200, 201]).toContain(target.status);
      const targetId = apiClient.track('materials', target.data.id);

      const created = await apiClient.createMaterial({
        name: `Source ${source} ${Date.now()}`, type, ...data(targetId)
      });
      expect([200, 201]).toContain(created.status);
      const sourceId = apiClient.track('materials', created.data.id);

      const relatedIds = async () => {
        const detail = await apiClient.getMaterialDetail(sourceId);
        expect(detail.status).toBe(200);
        return related(detail.data).map(material => String(material.id));
      };
      expect(await relatedIds()).toEqual([String(targetId)]);

      const update = await apiClient.updateMaterial(targetId, {
        name: targetName, type: 'default', description: 'Updated target'
      });
      expect([200, 204]).toContain(update.status);
      expect(await relatedIds()).toEqual([String(targetId)]);
    });

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
