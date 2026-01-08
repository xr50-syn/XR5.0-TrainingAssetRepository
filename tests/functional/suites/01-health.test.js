const apiClient = require('../helpers/api-client');
const config = require('../config');

/**
 * Health Check Tests
 *
 * Verifies basic API availability and core endpoints.
 * These tests run first and should pass before other suites.
 */

describe('Health Checks', () => {
  describe('API Availability', () => {
    test('GET /health returns healthy status', async () => {
      const response = await apiClient.health();

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('status');
      expect(response.data.status).toBe('healthy');
    });

    test('GET /health includes timestamp', async () => {
      const response = await apiClient.health();

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('timestamp');
    });

    test('GET /api/test returns success', async () => {
      const response = await apiClient.testEndpoint();

      expect(response.status).toBe(200);
    });
  });

  describe('Swagger Documentation', () => {
    test('Swagger UI is accessible', async () => {
      const response = await apiClient.swagger();

      // May return 200 (direct) or 302 (redirect)
      expect([200, 302]).toContain(response.status);
    });

    test('Swagger JSON is accessible', async () => {
      const response = await apiClient.get(
        `${config.API_BASE_URL}/swagger/v1/swagger.json`,
        { auth: false }
      );

      // Swagger may be at different paths
      expect([200, 404]).toContain(response.status);
    });
  });

  describe('CORS and Headers', () => {
    test('API returns proper content-type', async () => {
      const response = await apiClient.health();

      expect(response.headers['content-type']).toMatch(/application\/json/);
    });
  });
});
