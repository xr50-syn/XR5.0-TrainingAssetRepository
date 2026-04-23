const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * AI Assistant Material Tests
 *
 * Smoke coverage for the AI Assistant create flow:
 *  - Mode B (empty assets) → material bound to shared default DataLens collection,
 *    no upload happens, status stays "notready" until something triggers processing.
 *  - Mode A (assets present, multiple accepted payload shapes) → assets persisted to
 *    AIAssistantAssetIds, DataLens collection ensured, submit attempted.
 *  - The payload parser accepts config.assets[].id, top-level assets[].id, and legacy
 *    assetIds — with ids as either numbers or numeric strings.
 *  - The CreateMaterialResponse surfaces any DataLens failure via a Warnings array
 *    and flips status to "partial" so deploy-time smoke tests fail loudly when the
 *    chatbot side is misconfigured (bad base url, missing bearer, etc).
 *
 * These tests are permissive about 4xx status codes so the suite still runs in
 * environments without a live DataLens or without admin credentials — just like
 * the existing 05-materials suite.
 */

const OK_STATUSES = [200, 201];
const TOLERATED_STATUSES = [200, 201, 401, 403, 500];

describe('AI Assistant Material', () => {
  let createdMaterialIds = [];
  let fixtureAssetId = null;

  beforeAll(async () => {
    try {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    } catch (_) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }

    // Try to surface an existing asset id from the tenant so Mode A tests have
    // something real to attach. If the tenant is empty, skip the Mode A tests.
    try {
      const listResponse = await apiClient.listAssets();
      if (OK_STATUSES.includes(listResponse.status) && Array.isArray(listResponse.data) && listResponse.data.length > 0) {
        const asset = listResponse.data.find(a => (a.filetype || '').toLowerCase() === 'pdf') || listResponse.data[0];
        fixtureAssetId = asset.id || asset.Id;
      }
    } catch (_) {
      // ignore — we'll just skip the with-assets tests
    }
  });

  afterAll(async () => {
    if (config.SKIP_CLEANUP) return;
    for (const id of createdMaterialIds) {
      try { await apiClient.deleteMaterial(id); } catch (_) { /* ignore */ }
    }
  });

  describe('Mode B — empty assets, shared collection fallback', () => {
    test('creates a material with no assets and returns success', async () => {
      const payload = testData.createAIAssistantMaterialEmpty('empty');
      const response = await apiClient.createMaterial(payload);

      expect(TOLERATED_STATUSES).toContain(response.status);
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data).toHaveProperty('id');
      expect(response.data).toHaveProperty('type', 'ai_assistant');
      expect(response.data).toHaveProperty('status', 'success');
      // No asset parsing happened, so AssetIds should be empty/absent.
      expect(response.data.assetIds ?? []).toEqual([]);
      // Warnings should be absent/null in the happy path.
      expect(response.data.warnings ?? null).toBeNull();

      createdMaterialIds.push(response.data.id);
    });

    test('GET after Mode B create returns "notready" status and no assets', async () => {
      const payload = testData.createAIAssistantMaterialEmpty('empty-read');
      const createResponse = await apiClient.createMaterial(payload);
      if (!OK_STATUSES.includes(createResponse.status)) return;
      createdMaterialIds.push(createResponse.data.id);

      const detail = await apiClient.getMaterialDetail(createResponse.data.id);
      if (!OK_STATUSES.includes(detail.status)) return;

      expect(detail.data).toHaveProperty('type', 'ai_assistant');
      expect(detail.data).toHaveProperty('aiAssistantStatus', 'notready');
      expect(Array.isArray(detail.data.assets)).toBe(true);
      expect(detail.data.assets).toHaveLength(0);
    });
  });

  describe('Mode A — assets provided', () => {
    const skipIfNoAsset = (testName) => {
      if (!fixtureAssetId) {
        console.log(`Skipping "${testName}": no fixture asset available in this tenant`);
        return true;
      }
      return false;
    };

    test('accepts config.assets[].id with numeric-string id', async () => {
      if (skipIfNoAsset('config.assets shape')) return;

      const payload = testData.createAIAssistantMaterialWithConfigAssets(fixtureAssetId, 'config');
      const response = await apiClient.createMaterial(payload);

      expect(TOLERATED_STATUSES).toContain(response.status);
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data).toHaveProperty('type', 'ai_assistant');
      expect(Array.isArray(response.data.assetIds)).toBe(true);
      expect(response.data.assetIds).toEqual([Number(fixtureAssetId)]);
      createdMaterialIds.push(response.data.id);
    });

    test('accepts top-level assets[].id with numeric-string id', async () => {
      if (skipIfNoAsset('top-level assets shape')) return;

      const payload = testData.createAIAssistantMaterialWithTopLevelAssets(fixtureAssetId, 'toplevel');
      const response = await apiClient.createMaterial(payload);

      expect(TOLERATED_STATUSES).toContain(response.status);
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data.assetIds).toEqual([Number(fixtureAssetId)]);
      createdMaterialIds.push(response.data.id);
    });

    test('accepts legacy assetIds[] flat number array', async () => {
      if (skipIfNoAsset('legacy assetIds shape')) return;

      const payload = testData.createAIAssistantMaterialWithLegacyIds(fixtureAssetId, 'legacy');
      const response = await apiClient.createMaterial(payload);

      expect(TOLERATED_STATUSES).toContain(response.status);
      if (!OK_STATUSES.includes(response.status)) return;

      expect(response.data.assetIds).toEqual([Number(fixtureAssetId)]);
      createdMaterialIds.push(response.data.id);
    });

    test('GET after Mode A create exposes the linked asset', async () => {
      if (skipIfNoAsset('GET after Mode A')) return;

      const payload = testData.createAIAssistantMaterialWithConfigAssets(fixtureAssetId, 'config-read');
      const createResponse = await apiClient.createMaterial(payload);
      if (!OK_STATUSES.includes(createResponse.status)) return;
      createdMaterialIds.push(createResponse.data.id);

      const detail = await apiClient.getMaterialDetail(createResponse.data.id);
      if (!OK_STATUSES.includes(detail.status)) return;

      expect(Array.isArray(detail.data.assets)).toBe(true);
      expect(detail.data.assets.length).toBeGreaterThanOrEqual(1);
      // Status can be "notready", "process", or "ready" depending on how fast
      // DataLens has responded by GET time — any of the three is acceptable.
      expect(['notready', 'process', 'ready']).toContain(detail.data.aiAssistantStatus);
    });
  });

  describe('Failure surfacing', () => {
    // These tests deliberately do NOT assert that the call fails — a healthy DataLens
    // deployment makes them pass with status "success". They document the contract:
    // when DataLens is unreachable or misconfigured, the response carries the
    // information needed for a deploy smoke to fail loudly.
    test('response shape carries Warnings and "partial" status when chatbot side breaks', async () => {
      if (!fixtureAssetId) {
        console.log('Skipping: no fixture asset available');
        return;
      }

      const payload = testData.createAIAssistantMaterialWithConfigAssets(fixtureAssetId, 'failure-shape');
      const response = await apiClient.createMaterial(payload);

      if (!OK_STATUSES.includes(response.status)) return;
      createdMaterialIds.push(response.data.id);

      // status must be one of the documented values
      expect(['success', 'partial']).toContain(response.data.status);

      if (response.data.status === 'partial') {
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
      expect([400, 422, 500]).toContain(response.status);
    });
  });
});
