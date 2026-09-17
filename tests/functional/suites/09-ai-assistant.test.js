const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * AI Assistant Material Tests
 *
 * Smoke coverage for the AI Assistant create flow:
 *  - Mode B (empty assets) → material gets its own aiassist_{id} DataLens collection,
 *    no upload happens, status stays "notready" until something triggers processing.
 *  - Mode A (assets present, multiple accepted payload shapes) → assets persisted to
 *    AIAssistantAssetIds, DataLens collection ensured, submit attempted.
 *  - The payload parser accepts config.assets[].id, top-level assets[].id, and legacy
 *    assetIds — with ids as either numbers or numeric strings.
 *  - The CreateMaterialResponse surfaces any DataLens failure via a Warnings array
 *    and flips status to "partial" so deploy-time smoke tests fail loudly when the
 *    chatbot side is misconfigured (bad base url, missing bearer, etc).
 *
 * The suite uploads its own PDF fixture, so Mode A always runs instead of depending on
 * whatever assets the tenant happens to hold.
 *
 * A 500 is tolerated only when the AI assistant health check reports DataLens unavailable,
 * so the suite still runs in environments without a DataLens backend. With DataLens up, a
 * 500 is a failure. 401/403 are never tolerated: an authorization outcome must fail the
 * suite loudly.
 */

const OK_STATUSES = [200, 201];

describe('AI Assistant Material', () => {
  let fixtureAssetId;
  let dataLensAvailable = false;

  // Accepted statuses for calls that reach DataLens.
  const reachableStatuses = () => (dataLensAvailable ? OK_STATUSES : [...OK_STATUSES, 500]);

  const createTracked = async (payload) => {
    const response = await apiClient.createMaterial(payload);
    if (!OK_STATUSES.includes(response.status)) {
      apiClient.logResponse(response, 'CREATE AI ASSISTANT');
    }
    expect(reachableStatuses()).toContain(response.status);
    if (OK_STATUSES.includes(response.status)) {
      apiClient.track('materials', response.data.id);
    }
    return response;
  };

  beforeAll(async () => {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);

    const health = await apiClient.get(`${config.API_BASE_URL}/api/${config.getEffectiveTenant()}/ai-assistant/health`);
    expect(health.status).toBe(200);
    dataLensAvailable = health.data.available === true;
    if (!dataLensAvailable) {
      console.log('DataLens reports unavailable: tolerating 500 on calls that reach it.');
    }

    const pdf = testData.createTestPdfFile();
    const upload = await apiClient.uploadBuffer(config.ASSETS_API_URL, pdf.buffer, pdf.filename, {
      description: 'AI Assistant fixture document',
      filetype: 'pdf'
    });
    if (!OK_STATUSES.includes(upload.status)) {
      apiClient.logResponse(upload, 'UPLOAD PDF FIXTURE');
    }
    expect(OK_STATUSES).toContain(upload.status);
    fixtureAssetId = apiClient.track('assets', upload.data.id);
  });

  afterAll(async () => {
    // Force-delete the fixture asset first, while the AI Assistant materials still reference
    // it: that cascade is what removes the materials' DataLens documents and collections.
    // Deleting the materials first would leave them behind in DataLens.
    if (fixtureAssetId && !config.SKIP_CLEANUP) {
      const response = await apiClient.deleteAsset(fixtureAssetId, { force: true });
      expect([200, 204, 404]).toContain(response.status);
    }
    expect(await apiClient.cleanupTracked()).toEqual([]);
  });

  describe('Mode B — empty assets, per-material collection', () => {
    test('creates a material with no assets and returns success', async () => {
      const response = await createTracked(testData.createAIAssistantMaterialEmpty('empty'));
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data).toHaveProperty('id');
      expect(response.data).toHaveProperty('type', 'ai_assistant');
      expect(response.data).toHaveProperty('status', 'success');
      // No asset parsing happened, so AssetIds should be empty/absent.
      expect(response.data.assetIds ?? []).toEqual([]);
      // Warnings should be absent/null in the happy path.
      expect(response.data.warnings ?? null).toBeNull();
    });

    test('GET after Mode B create returns "notready" status and no assets', async () => {
      const createResponse = await createTracked(testData.createAIAssistantMaterialEmpty('empty-read'));
      if (!OK_STATUSES.includes(createResponse.status)) return;

      const detail = await apiClient.getMaterialDetail(createResponse.data.id);
      expect(detail.status).toBe(200);

      expect(detail.data).toHaveProperty('type', 'ai_assistant');
      expect(detail.data).toHaveProperty('aiAssistantStatus', 'notready');
      expect(Array.isArray(detail.data.assets)).toBe(true);
      expect(detail.data.assets).toHaveLength(0);
    });
  });

  describe('Mode A — assets provided', () => {
    test('accepts config.assets[].id with numeric-string id', async () => {
      const response = await createTracked(
        testData.createAIAssistantMaterialWithConfigAssets(fixtureAssetId, 'config'));
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data).toHaveProperty('type', 'ai_assistant');
      expect(Array.isArray(response.data.assetIds)).toBe(true);
      expect(response.data.assetIds).toEqual([String(fixtureAssetId)]);
    });

    test('accepts top-level assets[].id with numeric-string id', async () => {
      const response = await createTracked(
        testData.createAIAssistantMaterialWithTopLevelAssets(fixtureAssetId, 'toplevel'));
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data.assetIds).toEqual([String(fixtureAssetId)]);
    });

    test('accepts legacy assetIds[] flat number array', async () => {
      const response = await createTracked(
        testData.createAIAssistantMaterialWithLegacyIds(fixtureAssetId, 'legacy'));
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data.assetIds).toEqual([String(fixtureAssetId)]);
    });

    test('GET after Mode A create exposes the linked asset', async () => {
      const createResponse = await createTracked(
        testData.createAIAssistantMaterialWithConfigAssets(fixtureAssetId, 'config-read'));
      if (!OK_STATUSES.includes(createResponse.status)) return;

      const detail = await apiClient.getMaterialDetail(createResponse.data.id);
      expect(detail.status).toBe(200);

      expect(Array.isArray(detail.data.assets)).toBe(true);
      expect(detail.data.assets.length).toBeGreaterThanOrEqual(1);
      // Status can be "notready", "process", or "ready" depending on how fast
      // DataLens has responded by GET time — any of the three is acceptable.
      expect(['notready', 'process', 'ready']).toContain(detail.data.aiAssistantStatus);
    });
  });

  describe('Failure surfacing', () => {
    // This test deliberately does NOT assert that the call fails — a healthy DataLens
    // deployment makes it pass with status "success". It documents the contract:
    // when DataLens is unreachable or misconfigured, the response carries the
    // information needed for a deploy smoke to fail loudly.
    test('response shape carries Warnings and "partial" status when chatbot side breaks', async () => {
      const response = await createTracked(
        testData.createAIAssistantMaterialWithConfigAssets(fixtureAssetId, 'failure-shape'));
      if (!OK_STATUSES.includes(response.status)) return;

      // status must be one of the documented values
      expect(['success', 'partial']).toContain(response.data.status);

      if (response.data.status === 'partial') {
        console.log('AI Assistant create reported partial:', JSON.stringify(response.data.warnings));
        // warnings[] MUST be populated so CI can grep for chatbot failures
        expect(Array.isArray(response.data.warnings)).toBe(true);
        expect(response.data.warnings.length).toBeGreaterThan(0);
      } else {
        expect(response.data.warnings ?? null).toBeNull();
      }
    });
  });

  describe('Validation', () => {
    test('rejects AI Assistant material without a name', async () => {
      const response = await apiClient.createMaterial({
        type: 'ai_assistant',
        description: 'missing name'
      });
      // Name validation runs before any DataLens call, so no 500 tolerance is needed here.
      expect([400, 422]).toContain(response.status);
    });
  });
});
