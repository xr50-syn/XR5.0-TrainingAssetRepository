const apiClient = require('../helpers/api-client');
const config = require('../config');

/**
 * Authentication Tests
 *
 * Verifies Keycloak authentication flow and token handling.
 */

describe('Authentication', () => {
  // Skip auth tests if Keycloak is not available or NO_AUTH mode is enabled
  const skipIfNoKeycloak = () => {
    if (config.NO_AUTH) {
      return true;
    }
    if (global.__TEST_CONFIG__?.keycloakAvailable === false) {
      return true;
    }
    return false;
  };

  // Skip reason message for logging
  const getSkipReason = () => {
    if (config.NO_AUTH) return 'NO_AUTH mode enabled';
    if (global.__TEST_CONFIG__?.keycloakAvailable === false) return 'Keycloak not available';
    return null;
  };

  describe('Token Acquisition', () => {
    test('can obtain token with valid credentials', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      const tokenResponse = await apiClient.authenticate(
        config.TEST_USER,
        config.TEST_PASSWORD
      );

      expect(tokenResponse).toHaveProperty('access_token');
      expect(tokenResponse).toHaveProperty('token_type', 'Bearer');
      expect(tokenResponse).toHaveProperty('expires_in');
      expect(tokenResponse.expires_in).toBeGreaterThan(0);
    });

    test('token contains expected claims', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);

      // Decode JWT (without verification - just for inspection)
      const token = apiClient.token;
      const parts = token.split('.');
      expect(parts.length).toBe(3);

      const payload = JSON.parse(Buffer.from(parts[1], 'base64').toString());

      expect(payload).toHaveProperty('sub');
      expect(payload).toHaveProperty('exp');
      expect(payload).toHaveProperty('iat');
    });

    test('rejects invalid credentials', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      const axios = require('axios');
      const params = new URLSearchParams();
      params.append('grant_type', 'password');
      params.append('client_id', config.KEYCLOAK_CLIENT_ID);
      params.append('username', 'invalid-user');
      params.append('password', 'wrong-password');

      const response = await axios.post(config.KEYCLOAK_TOKEN_URL, params, {
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        validateStatus: () => true
      });

      expect(response.status).toBe(401);
    });
  });

  describe('Protected Endpoints', () => {
    test('authenticated request succeeds', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);

      // Try accessing a protected endpoint (list tenants)
      const response = await apiClient.listTenants();

      // Should get 200 or 403 (forbidden if not admin), not 401
      expect([200, 403]).toContain(response.status);
    });

    test('unauthenticated request is rejected', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      // Clear any existing token
      apiClient.token = null;

      const response = await apiClient.get(config.TENANT_API_URL, { auth: false });

      // Should get 401 Unauthorized
      expect([401, 403]).toContain(response.status);
    });
  });

  describe('Token Validation', () => {
    test('expired token is rejected', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      // Use a known-expired token (this is a test JWT that's already expired)
      const expiredToken = 'eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE2MDAwMDAwMDAsImlhdCI6MTYwMDAwMDAwMCwic3ViIjoidGVzdCJ9.invalid';

      const response = await apiClient.get(config.TENANT_API_URL, {
        auth: false,
        headers: { 'Authorization': `Bearer ${expiredToken}` }
      });

      expect([401, 403]).toContain(response.status);
    });

    test('malformed token is rejected', async () => {
      const skipReason = getSkipReason();
      if (skipReason) {
        console.log(`Skipping: ${skipReason}`);
        return;
      }

      const response = await apiClient.get(config.TENANT_API_URL, {
        auth: false,
        headers: { 'Authorization': 'Bearer not-a-valid-token' }
      });

      expect([401, 403]).toContain(response.status);
    });
  });
});
