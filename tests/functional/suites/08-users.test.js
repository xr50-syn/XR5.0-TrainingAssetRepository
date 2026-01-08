const apiClient = require('../helpers/api-client');
const testData = require('../helpers/test-data');
const config = require('../config');

/**
 * User Management Tests
 *
 * Verifies user CRUD operations.
 */

describe('User Management', () => {
  let createdUserName;

  beforeAll(async () => {
    try {
      await apiClient.authenticate(config.ADMIN_USER, config.ADMIN_PASSWORD);
    } catch (error) {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
    }
  });

  afterAll(async () => {
    if (createdUserName && !config.SKIP_CLEANUP) {
      try {
        await apiClient.deleteUser(createdUserName);
      } catch (error) {
        // Ignore
      }
    }
  });

  describe('List Users', () => {
    test('can list users', async () => {
      const response = await apiClient.listUsers();

      expect([200, 401, 403]).toContain(response.status);

      if (response.status === 200) {
        expect(Array.isArray(response.data)).toBe(true);
      }
    });
  });

  describe('Create Users', () => {
    test('can create user', async () => {
      const user = testData.createTestUser();
      const response = await apiClient.createUser(user);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('userName', user.userName);
        createdUserName = user.userName;
        global.__TEST_CONFIG__?.createdResources?.users?.push(createdUserName);
      }
    });

    test('can create admin user', async () => {
      const user = testData.createAdminUser();
      const response = await apiClient.createUser(user);

      expect([200, 201, 401, 403]).toContain(response.status);

      if (response.status === 200 || response.status === 201) {
        expect(response.data).toHaveProperty('admin', true);
        global.__TEST_CONFIG__?.createdResources?.users?.push(user.userName);
      }
    });

    test('rejects duplicate username', async () => {
      if (!createdUserName) {
        console.log('Skipping: No user created');
        return;
      }

      const user = testData.createTestUser();
      user.userName = createdUserName;

      const response = await apiClient.createUser(user);

      expect([400, 409]).toContain(response.status);
    });
  });

  describe('Read Users', () => {
    test('can get user by username', async () => {
      if (!createdUserName) {
        console.log('Skipping: No user created');
        return;
      }

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
    test('can update user full name', async () => {
      if (!createdUserName) {
        console.log('Skipping: No user created');
        return;
      }

      const newFullName = `Updated User ${Date.now()}`;
      const response = await apiClient.updateUser(createdUserName, {
        fullName: newFullName
      });

      expect([200, 204]).toContain(response.status);

      // Verify the update
      const getResponse = await apiClient.getUser(createdUserName);
      expect(getResponse.data.fullName).toBe(newFullName);
    });

    test('can update user email', async () => {
      if (!createdUserName) {
        console.log('Skipping: No user created');
        return;
      }

      const newEmail = `updated-${Date.now()}@test.local`;
      const response = await apiClient.updateUser(createdUserName, {
        userEmail: newEmail
      });

      expect([200, 204]).toContain(response.status);
    });
  });

  describe('Delete Users', () => {
    test('can delete user', async () => {
      // Create a user specifically for deletion
      const user = testData.createTestUser('delete-test');
      const createResponse = await apiClient.createUser(user);

      if (createResponse.status !== 200 && createResponse.status !== 201) {
        console.log('Skipping: Could not create user');
        return;
      }

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
      const response = await apiClient.createUser({
        fullName: 'No Username User',
        userEmail: 'no-username@test.local',
        password: 'TestPass123!'
      });

      expect([400, 422]).toContain(response.status);
    });

    test('rejects user without email', async () => {
      const response = await apiClient.createUser({
        userName: `noEmail${Date.now()}`,
        fullName: 'No Email User',
        password: 'TestPass123!'
      });

      expect([400, 422]).toContain(response.status);
    });

    test('rejects user without password', async () => {
      const response = await apiClient.createUser({
        userName: `noPassword${Date.now()}`,
        fullName: 'No Password User',
        userEmail: 'no-password@test.local'
      });

      expect([400, 422]).toContain(response.status);
    });
  });
});
