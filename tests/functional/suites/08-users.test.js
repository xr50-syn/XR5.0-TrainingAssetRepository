const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * User Management Tests
 *
 * Verifies user CRUD operations.
 *
 * The suite authenticates as ADMIN_USER, which must be a tenant admin at least, so a 401/403
 * on ordinary user management is a failure. The one role-dependent rule is creating a
 * system administrator: only a system admin may, so that expectation follows the caller's
 * identity as reported by /api/auth/me.
 */

describe('User Management', () => {
  let createdUserName;
  let callerIsSystemAdmin;

  beforeAll(async () => {
    await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);

    // NO_AUTH runs rely on the development bypass, which grants every policy.
    const me = await apiClient.get(`${config.API_BASE_URL}/api/auth/me`);
    callerIsSystemAdmin = config.NO_AUTH || (me.status === 200 && me.data.isSystemAdmin === true);
  });

  afterAll(async () => {
    expect(await apiClient.cleanupTracked()).toEqual([]);
  });

  describe('List Users', () => {
    test('can list users', async () => {
      const response = await apiClient.listUsers();

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
    });
  });

  describe('Create Users', () => {
    // Helper to log user operation failures
    const logUserFailure = (user, response, testName) => {
      if (response.status >= 400 || config.DEBUG) {
        console.log(`\n--- ${testName} ---`);
        console.log('Request:', JSON.stringify(user, null, 2));
        apiClient.logResponse(response, testName);
        console.log('---\n');
      }
    };

    test('can create user', async () => {
      const user = testData.createTestUser();
      const response = await apiClient.createUser(user);

      logUserFailure(user, response, 'CREATE USER');
      expect([200, 201]).toContain(response.status);
      expect(response.data).toHaveProperty('userName', user.userName);
      createdUserName = apiClient.track('users', user.userName);
    });

    test('creating a system admin user requires a system administrator', async () => {
      const user = testData.createAdminUser();
      const response = await apiClient.createUser(user);

      if (callerIsSystemAdmin) {
        logUserFailure(user, response, 'CREATE ADMIN USER');
        expect([200, 201]).toContain(response.status);
        expect(response.data).toHaveProperty('admin', true);
        apiClient.track('users', user.userName);
      } else {
        expect(response.status).toBe(403);
        const getResponse = await apiClient.getUser(user.userName);
        expect(getResponse.status).toBe(404);
      }
    });

    test('rejects duplicate username', async () => {
      expect(createdUserName).toBeDefined();

      const user = testData.createTestUser();
      user.userName = createdUserName;

      const response = await apiClient.createUser(user);

      logUserFailure(user, response, 'DUPLICATE USER');
      expect(response.status).toBe(409);
    });
  });

  describe('Read Users', () => {
    test('can get user by username', async () => {
      expect(createdUserName).toBeDefined();

      const response = await apiClient.getUser(createdUserName);

      expect(response.status).toBe(200);
      expect(response.data).toHaveProperty('userName', createdUserName);
    });

    test('returns 404 for non-existent user', async () => {
      const response = await apiClient.getUser('non-existent-user-xyz');

      expect(response.status).toBe(404);
    });
  });

  describe('Update Users', () => {
    // PUT on a user is a partial update: fields left out of the body are kept.
    test('can update user full name', async () => {
      expect(createdUserName).toBeDefined();

      const newFullName = `Updated User ${Date.now()}`;
      const response = await apiClient.updateUser(createdUserName, { fullName: newFullName });

      if (response.status >= 400) {
        apiClient.logResponse(response, 'UPDATE FULL NAME');
      }
      expect([200, 204]).toContain(response.status);

      const getResponse = await apiClient.getUser(createdUserName);
      expect(getResponse.status).toBe(200);
      expect(getResponse.data.fullName).toBe(newFullName);
    });

    test('can update user email', async () => {
      expect(createdUserName).toBeDefined();

      const newEmail = `updated-${Date.now()}@test.local`;
      const response = await apiClient.updateUser(createdUserName, { userEmail: newEmail });

      if (response.status >= 400) {
        apiClient.logResponse(response, 'UPDATE EMAIL');
      }
      expect([200, 204]).toContain(response.status);

      const getResponse = await apiClient.getUser(createdUserName);
      expect(getResponse.status).toBe(200);
      expect(getResponse.data.userEmail).toBe(newEmail);
    });
  });

  describe('Delete Users', () => {
    test('can delete user', async () => {
      // Create a user specifically for deletion
      const user = testData.createTestUser('delete-test');
      const createResponse = await apiClient.createUser(user);

      expect([200, 201]).toContain(createResponse.status);
      // Tracked so a failed delete below still gets cleaned up.
      apiClient.track('users', user.userName);

      // Delete it
      const deleteResponse = await apiClient.deleteUser(user.userName);
      expect([200, 204]).toContain(deleteResponse.status);

      // Verify it's gone
      const getResponse = await apiClient.getUser(user.userName);
      expect([404, 410]).toContain(getResponse.status);
    });
  });

  describe('User Validation', () => {
    test('rejects user without username', async () => {
      const userData = {
        fullName: 'No Username User',
        userEmail: 'no-username@test.local',
        password: 'TestPass123!'
      };
      const response = await apiClient.createUser(userData);

      expect(response.status).toBe(400);
    });

    // Identities the XR5.0 Hub authenticates, service accounts in particular, are provisioned
    // by their Hub user id and may carry no e-mail, so a missing e-mail is accepted.
    test('accepts user without email', async () => {
      const userData = {
        userName: `noEmail${Date.now()}`,
        fullName: 'No Email User',
        password: 'TestPass123!'
      };
      const response = await apiClient.createUser(userData);

      expect([200, 201]).toContain(response.status);
      apiClient.track('users', userData.userName);
    });

    // A password only matters where storage mirrors users into its own account store
    // (OwnCloud). Everywhere else authentication is the identity provider's job.
    test('requires a password only for OwnCloud storage', async () => {
      const userData = {
        userName: `noPassword${Date.now()}`,
        fullName: 'No Password User',
        userEmail: 'no-password@test.local'
      };
      const response = await apiClient.createUser(userData);

      if (testData.STORAGE_TYPE.toLowerCase() === 'owncloud') {
        expect(response.status).toBe(400);
      } else {
        expect([200, 201]).toContain(response.status);
        apiClient.track('users', userData.userName);
      }
    });
  });
});
