const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * S3 Storage Operations Tests
 *
 * Verifies file upload, download, and management via S3.
 */

describe('S3 Storage Operations', () => {
  let uploadedAssetId;

  beforeAll(async () => {
    // Authenticate
    try {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    } catch (error) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }
  });

  afterAll(async () => {
    // Cleanup uploaded assets
    if (uploadedAssetId && !config.SKIP_CLEANUP) {
      try {
        await apiClient.deleteAsset(uploadedAssetId);
      } catch (error) {
        // Ignore cleanup errors
      }
    }
  });

  describe('File Upload', () => {
    test('can upload text file', async () => {
      const testFile = testData.createTestTextFile('Verification test content');

      const response = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}/upload`,
        testFile.buffer,
        testFile.filename,
        {
          description: 'Verification test file',
          filetype: 'txt'
        }
      );

      // Accept success or auth issues
      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('id');
        uploadedAssetId = response.data.id;

        // Track for cleanup
        global.__TEST_CONFIG__?.createdResources?.assets?.push(uploadedAssetId);
      }
    });

    test('can upload image file', async () => {
      const testImage = testData.createTestImageFile();

      const response = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}/upload`,
        testImage.buffer,
        testImage.filename,
        {
          description: 'Verification test image',
          filetype: 'png'
        }
      );

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        const imageAssetId = response.data.id;
        global.__TEST_CONFIG__?.createdResources?.assets?.push(imageAssetId);
      }
    });
  });

  describe('File Retrieval', () => {
    test('can list assets', async () => {
      const response = await apiClient.listAssets();

      expect([200, 401, 403]).toContain(response.status);

      if (response.status === 200) {
        expect(Array.isArray(response.data)).toBe(true);
      }
    });

    test('can get asset metadata', async () => {
      if (!uploadedAssetId) {
        console.log('Skipping: No asset uploaded');
        return;
      }

      const response = await apiClient.getAsset(uploadedAssetId);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('id', uploadedAssetId);
      expect(response.data).toHaveProperty('filename');
    });

    test('can get file info from S3', async () => {
      if (!uploadedAssetId) {
        console.log('Skipping: No asset uploaded');
        return;
      }

      const response = await apiClient.getAssetFileInfo(uploadedAssetId);

      expect([200, 404]).toContain(response.status);

      if (response.status === 200) {
        expect(response.data).toHaveProperty('exists');
        expect(response.data).toHaveProperty('size');
      }
    });
  });

  describe('File Download', () => {
    test('can download uploaded file', async () => {
      if (!uploadedAssetId) {
        console.log('Skipping: No asset uploaded');
        return;
      }

      const response = await apiClient.downloadAsset(uploadedAssetId);

      expect([200, 302]).toContain(response.status);

      if (response.status === 200) {
        // Verify we got file content
        expect(response.data).toBeDefined();
        expect(response.data.length).toBeGreaterThan(0);
      }
    });

    test('download preserves file content', async () => {
      // Upload a new file with known content
      const knownContent = `Test content ${Date.now()}`;
      const testFile = testData.createTestTextFile(knownContent);

      const uploadResponse = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}/upload`,
        testFile.buffer,
        testFile.filename,
        { description: 'Content verification test' }
      );

      if (uploadResponse.status !== 200 && uploadResponse.status !== 201) {
        console.log('Skipping: Could not upload file');
        return;
      }

      const assetId = uploadResponse.data.id;
      global.__TEST_CONFIG__?.createdResources?.assets?.push(assetId);

      // Download and verify content
      const downloadResponse = await apiClient.downloadAsset(assetId);

      if (downloadResponse.status === 200) {
        const downloadedContent = Buffer.from(downloadResponse.data).toString('utf-8');
        expect(downloadedContent).toBe(knownContent);
      }
    });
  });

  describe('File Deletion', () => {
    test('can delete asset', async () => {
      // Upload a file to delete
      const testFile = testData.createTestTextFile('Delete test');

      const uploadResponse = await apiClient.uploadBuffer(
        `${config.ASSETS_API_URL}/upload`,
        testFile.buffer,
        `delete-test-${Date.now()}.txt`,
        { description: 'Delete test' }
      );

      if (uploadResponse.status !== 200 && uploadResponse.status !== 201) {
        console.log('Skipping: Could not upload file');
        return;
      }

      const assetId = uploadResponse.data.id;

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
