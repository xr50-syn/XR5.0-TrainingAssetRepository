const apiClient = require('../helpers/api-client');
const config = require('../config');

/**
 * Schema Migration Tests
 *
 * The test tenant was provisioned through the API during global setup, so its database was
 * built from the committed EF Core migrations. These tests verify the migration control plane:
 * the tenant is reported as managed and current, migrating it again is a no-op, unknown tenants
 * are 404, and the endpoints are system-admin only.
 */

describe('Schema Migrations', () => {
  const tenant = config.getEffectiveTenant();
  const authTests = config.NO_AUTH ? describe.skip : describe;

  beforeAll(async () => {
    await apiClient.authenticate(config.SYSADMIN_USER, config.SYSADMIN_PASSWORD);
  });

  describe('Status', () => {
    test('the test tenant is managed with nothing pending', async () => {
      const response = await apiClient.getMigrationStatus(tenant);

      expect(response.status).toBe(200);
      expect(Array.isArray(response.data)).toBe(true);
      expect(response.data).toHaveLength(1);

      const status = response.data[0];
      expect(status.target).toBe(`tenant:${tenant}@${status.databaseName}`);
      expect(status.state).toBe('managed');
      expect(status.pending).toEqual([]);
      expect(status.applied.length).toBeGreaterThan(0);
      expect(status.applied[0]).toMatch(/_Baseline$/);
    });

    test('the overall status lists the central database and the test tenant', async () => {
      const response = await apiClient.getMigrationStatus();

      expect(response.status).toBe(200);
      const targets = response.data.map(s => s.target);
      expect(targets.some(t => t.startsWith('registry@'))).toBe(true);
      expect(targets.some(t => t.startsWith('training@'))).toBe(true);
      expect(targets.some(t => t.startsWith(`tenant:${tenant}@`))).toBe(true);
      for (const status of response.data) {
        expect(status.state).toBe('managed');
        expect(status.pending).toEqual([]);
      }
    });

    test('an unregistered tenant is 404', async () => {
      const response = await apiClient.getMigrationStatus(`does_not_exist_${Date.now()}`);

      expect(response.status).toBe(404);
    });
  });

  describe('Migrate', () => {
    test('migrating a current tenant is idempotent', async () => {
      const first = await apiClient.migrateTenant(tenant);
      const second = await apiClient.migrateTenant(tenant);

      for (const response of [first, second]) {
        expect(response.status).toBe(200);
        expect(response.data.succeeded).toBe(true);
        expect(response.data.adopted).toBe(false);
        expect(response.data.stateBefore).toBe('managed');
        expect(response.data.appliedNow).toEqual([]);
      }
    });

    test('migrating an unregistered tenant is 404 and creates nothing', async () => {
      const name = `does_not_exist_${Date.now()}`;

      const response = await apiClient.migrateTenant(name);

      expect(response.status).toBe(404);
      const status = await apiClient.getMigrationStatus(name);
      expect(status.status).toBe(404);
    });

    test('the test tenant still answers after migration', async () => {
      const response = await apiClient.listMaterials();

      expect(response.status).toBe(200);
    });
  });

  authTests('Authorization', () => {
    test('anonymous callers are rejected', async () => {
      const response = await apiClient.get(
        `${config.API_BASE_URL}/api/troubleshooting/migration-status`, { auth: false });

      expect(response.status).toBe(401);
    });

    test('a regular user is forbidden', async () => {
      await apiClient.authenticate(config.TEST_USER, config.TEST_PASSWORD);
      try {
        const status = await apiClient.getMigrationStatus();
        const migrate = await apiClient.migrateTenant(tenant);

        expect(status.status).toBe(403);
        expect(migrate.status).toBe(403);
      } finally {
        await apiClient.authenticate(config.SYSADMIN_USER, config.SYSADMIN_PASSWORD);
      }
    });
  });
});
