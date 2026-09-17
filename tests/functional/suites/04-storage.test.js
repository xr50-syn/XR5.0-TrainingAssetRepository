const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * S3 Storage Operations Tests
 *
 * Verifies file upload, download, and management via S3.
 *
 * Tests that depend on an earlier upload fail when it is missing instead of returning early:
 * an early return counts as a pass, which hid broken paths behind a green run.
 */

describe('S3 Storage Operations', () => {
  let uploadedAssetId;

  beforeAll(async () => {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
  });

  afterAll(async () => {
    expect(await apiClient.cleanupTracked()).toEqual([]);
  });

  describe('File Upload', () => {
    test('can upload text file', async () => {
      const testFile = testData.createTestTextFile('Verification test content');

      const response = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}`,
        testFile.buffer,
        testFile.filename,
        {
          description: 'Verification test file',
          filetype: 'txt'
        }
      );

      if (response.status >= 400) {
        console.log('\n--- TEXT FILE UPLOAD FAILED ---');
        console.log('URL:', `${config.ASSETS_API_URL}`);
        console.log('Filename:', testFile.filename);
        console.log('Buffer size:', testFile.buffer.length);
        apiClient.logResponse(response, 'UPLOAD');
        console.log('---\n');
      }

      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('id');
      uploadedAssetId = apiClient.track('assets', response.data.id);
    });

    test('can upload image file', async () => {
      const testImage = testData.createTestImageFile();

      const response = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}`,
        testImage.buffer,
        testImage.filename,
        {
          description: 'Verification test image',
          filetype: 'png'
        }
      );

      if (response.status >= 400) {
        console.log('\n--- IMAGE UPLOAD FAILED ---');
        console.log('URL:', `${config.ASSETS_API_URL}`);
        console.log('Filename:', testImage.filename);
        console.log('Buffer size:', testImage.buffer.length);
        apiClient.logResponse(response, 'UPLOAD');
        console.log('---\n');
      }

      expect([200, 201]).toContain(response.status);
      apiClient.track('assets', response.data.id);
    });
  });

  describe('File Retrieval', () => {
    test('can list assets', async () => {
      const response = await apiClient.listAssets();

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });

    test('can get asset metadata', async () => {
      expect(uploadedAssetId).toBeDefined();

      const response = await apiClient.getAsset(uploadedAssetId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', uploadedAssetId);
      expect(response.data).toHaveProperty('filename');
    });

    test('can get file info from S3', async () => {
      expect(uploadedAssetId).toBeDefined();

      const response = await apiClient.getAssetFileInfo(uploadedAssetId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('fileExists');
      expect(response.data).toHaveProperty('fileSize');
    });
  });

  describe('File Download', () => {
    test('can download uploaded file', async () => {
      expect(uploadedAssetId).toBeDefined();

      const response = await apiClient.downloadAsset(uploadedAssetId);

      expect([200, 302]).toContain(response.status);

      if (response.status === 200) {
        // Verify we got file content
        expect(response.data).toBeDefined();
        expect(response.data.length).toBeGreaterThan(0);
      }
    });

    test('download returns a presigned URL for the uploaded file', async () => {
      // Upload a new file with known content
      const knownContent = `Test content ${Date.now()}`;
      const testFile = testData.createTestTextFile(knownContent);

      const uploadResponse = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}`,
        testFile.buffer,
        testFile.filename,
        { description: 'Content verification test' }
      );

      expect([200, 201]).toContain(uploadResponse.status);
      const assetId = apiClient.track('assets', uploadResponse.data.id);

      // GET /assets/{id}/download returns a presigned download URL (JSON), not the raw bytes.
      // (The URL points at the storage backend's internal host, so the byte round-trip is not
      // exercised from the host test runner here.)
      const downloadResponse = await apiClient.downloadAsset(assetId);
      expect([200, 302]).toContain(downloadResponse.status);

      if (downloadResponse.status === 200) {
        const body = JSON.parse(Buffer.from(downloadResponse.data).toString('utf-8'));
        const url = body.downloadUrl || body.DownloadUrl;
        expect(typeof url).toBe('string');
        expect(url.length).toBeGreaterThan(0);
      }
    });
  });

  describe('File Deletion', () => {
    test('can delete asset', async () => {
      // Upload a file to delete
      const testFile = testData.createTestTextFile('Delete test');

      const uploadResponse = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}`,
        testFile.buffer,
        `delete-test-${Date.now()}.txt`,
        { description: 'Delete test' }
      );

      expect([200, 201]).toContain(uploadResponse.status);
      // Tracked so a failed delete below still gets cleaned up.
      const assetId = apiClient.track('assets', uploadResponse.data.id);

      // Delete it
      const deleteResponse = await apiClient.deleteAsset(assetId);
      expect([200, 204]).toContain(deleteResponse.status);

      // Verify it's gone
      const getResponse = await apiClient.getAsset(assetId);
      expect([404, 410]).toContain(getResponse.status);
    });
  });

  describe('S3 Error Handling', () => {
    test('returns 404 for non-existent asset', async () => {
      const response = await apiClient.getAsset(999999);

      expect(response.status).toBe(404);
    });

    test('handles invalid asset ID gracefully', async () => {
      const response = await apiClient.getAsset('invalid-id');

      expect([400, 404]).toContain(response.status);
    });
  });
});
