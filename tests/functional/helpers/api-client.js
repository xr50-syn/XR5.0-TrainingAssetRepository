const axios = require('axios');
const config = require('../config');

/**
 * API Client with authentication support
 */
class ApiClient {
  constructor() {
    this.token = null;
    this.tokenExpiry = null;

    this.client = axios.create({
      timeout: config.REQUEST_TIMEOUT,
      validateStatus: () => true // Don't throw on non-2xx status
    });

    // Debug logging - always enabled for better test diagnostics
    this.client.interceptors.request.use(req => {
      if (config.DEBUG) {
        console.log(`\n[API REQUEST] ${req.method.toUpperCase()} ${req.url}`);
        if (req.data && typeof req.data === 'object' && !(req.data instanceof FormData)) {
          console.log('[API REQUEST BODY]', JSON.stringify(req.data, null, 2));
        }
        if (req.headers) {
          const headers = { ...req.headers };
          if (headers.Authorization) {
            headers.Authorization = headers.Authorization.substring(0, 20) + '...';
          }
          console.log('[API REQUEST HEADERS]', headers);
        }
      }
      return req;
    });

    this.client.interceptors.response.use(res => {
      if (config.DEBUG) {
        console.log(`[API RESPONSE] ${res.status} ${res.statusText}`);
        if (res.data) {
          const dataStr = typeof res.data === 'string'
            ? res.data.substring(0, 500)
            : JSON.stringify(res.data, null, 2).substring(0, 500);
          console.log('[API RESPONSE BODY]', dataStr);
        }
      }
      return res;
    });
  }

  /**
   * Get authentication token from Keycloak
   * If NO_AUTH mode is enabled, skip authentication
   */
  async authenticate(username = config.TEST_USER, password = config.TEST_PASSWORD) {
    // Skip authentication in NO_AUTH mode
    if (config.NO_AUTH) {
      this.token = null;
      this.tokenExpiry = null;
      return { noAuth: true };
    }

    const params = new URLSearchParams();
    params.append('grant_type', 'password');
    params.append('client_id', config.KEYCLOAK_CLIENT_ID);
    params.append('username', username);
    params.append('password', password);

    if (config.KEYCLOAK_CLIENT_SECRET) {
      params.append('client_secret', config.KEYCLOAK_CLIENT_SECRET);
    }

    const response = await this.client.post(config.KEYCLOAK_TOKEN_URL, params, {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' }
    });

    if (response.status !== 200) {
      throw new Error(`Authentication failed: ${response.status} - ${JSON.stringify(response.data)}`);
    }

    this.token = response.data.access_token;
    this.tokenExpiry = Date.now() + (response.data.expires_in * 1000);

    return response.data;
  }

  /**
   * Get headers with optional auth
   */
  getHeaders(includeAuth = true) {
    const headers = {
      'Content-Type': 'application/json'
    };

    if (includeAuth && this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }

    return headers;
  }

  /**
   * Check if token is still valid
   * Returns true in NO_AUTH mode
   */
  isAuthenticated() {
    if (config.NO_AUTH) {
      return true;
    }
    return this.token && this.tokenExpiry && Date.now() < this.tokenExpiry;
  }

  // HTTP Methods

  async get(url, options = {}) {
    return this.client.get(url, {
      headers: this.getHeaders(options.auth !== false),
      ...options
    });
  }

  async post(url, data, options = {}) {
    return this.client.post(url, data, {
      headers: this.getHeaders(options.auth !== false),
      ...options
    });
  }

  async put(url, data, options = {}) {
    return this.client.put(url, data, {
      headers: this.getHeaders(options.auth !== false),
      ...options
    });
  }

  async delete(url, options = {}) {
    return this.client.delete(url, {
      headers: this.getHeaders(options.auth !== false),
      ...options
    });
  }

  /**
   * Upload file with multipart form data
   */
  async uploadFile(url, filePath, additionalFields = {}) {
    const FormData = require('form-data');
    const fs = require('fs');
    const path = require('path');

    const form = new FormData();

    // Add file
    form.append('file', fs.createReadStream(filePath));

    // Add additional fields
    for (const [key, value] of Object.entries(additionalFields)) {
      form.append(key, value);
    }

    return this.client.post(url, form, {
      headers: {
        ...this.getHeaders(),
        ...form.getHeaders()
      }
    });
  }

  /**
   * Upload file from buffer (for test files)
   */
  async uploadBuffer(url, buffer, filename, additionalFields = {}) {
    const FormData = require('form-data');

    const form = new FormData();
    form.append('file', buffer, { filename });

    for (const [key, value] of Object.entries(additionalFields)) {
      form.append(key, value);
    }

    return this.client.post(url, form, {
      headers: {
        ...this.getHeaders(),
        ...form.getHeaders()
      }
    });
  }

  // Convenience methods for common endpoints

  async health() {
    return this.get(`${config.API_BASE_URL}/health`, { auth: false });
  }

  async testEndpoint() {
    return this.get(`${config.API_BASE_URL}/api/test`, { auth: false });
  }

  async swagger() {
    return this.get(`${config.API_BASE_URL}/swagger/index.html`, { auth: false });
  }

  // Tenant operations

  async listTenants() {
    return this.get(config.TENANT_API_URL);
  }

  async getTenant(tenantName) {
    return this.get(`${config.TENANT_API_URL}/${tenantName}`);
  }

  async createTenant(tenantData) {
    return this.post(config.TENANT_API_URL, tenantData);
  }

  async deleteTenant(tenantName) {
    return this.delete(`${config.TENANT_API_URL}/${tenantName}`);
  }

  async validateStorage(tenantName) {
    return this.get(`${config.TENANT_API_URL}/${tenantName}/validate-storage`);
  }

  async getStorageStats(tenantName) {
    return this.get(`${config.TENANT_API_URL}/${tenantName}/storage-stats`);
  }

  // Material operations (uses configured tenant)

  async listMaterials() {
    return this.get(config.MATERIALS_API_URL);
  }

  async getMaterial(id) {
    return this.get(`${config.MATERIALS_API_URL}/${id}`);
  }

  async getMaterialDetail(id) {
    return this.get(`${config.MATERIALS_API_URL}/${id}/detail`);
  }

  async createMaterial(materialData) {
    return this.post(config.MATERIALS_API_URL, materialData);
  }

  async updateMaterial(id, materialData) {
    return this.put(`${config.MATERIALS_API_URL}/${id}`, materialData);
  }

  async deleteMaterial(id) {
    return this.delete(`${config.MATERIALS_API_URL}/${id}`);
  }

  async getMaterialChildren(id) {
    return this.get(`${config.MATERIALS_API_URL}/${id}/children`);
  }

  async getMaterialParents(id) {
    return this.get(`${config.MATERIALS_API_URL}/${id}/parents`);
  }

  async assignMaterialChild(parentId, childId) {
    return this.post(`${config.MATERIALS_API_URL}/${parentId}/assign-material/${childId}`);
  }

  // Asset operations

  async listAssets() {
    return this.get(config.ASSETS_API_URL);
  }

  async getAsset(id) {
    return this.get(`${config.ASSETS_API_URL}/${id}`);
  }

  async getAssetFileInfo(id) {
    return this.get(`${config.ASSETS_API_URL}/${id}/file-info`);
  }

  async downloadAsset(id) {
    return this.get(`${config.ASSETS_API_URL}/${id}/download`, {
      responseType: 'arraybuffer'
    });
  }

  async deleteAsset(id) {
    return this.delete(`${config.ASSETS_API_URL}/${id}`);
  }

  // Program operations

  async listPrograms() {
    return this.get(config.PROGRAMS_API_URL);
  }

  async getProgram(id) {
    return this.get(`${config.PROGRAMS_API_URL}/${id}`);
  }

  async getProgramDetail(id) {
    return this.get(`${config.PROGRAMS_API_URL}/${id}/detail`);
  }

  async createProgram(programData) {
    return this.post(config.PROGRAMS_API_URL, programData);
  }

  async deleteProgram(id) {
    return this.delete(`${config.PROGRAMS_API_URL}/${id}`);
  }

  async assignMaterialToProgram(programId, materialId) {
    return this.post(`${config.PROGRAMS_API_URL}/${programId}/assign-material/${materialId}`);
  }

  // User operations

  async listUsers() {
    return this.get(config.USERS_API_URL);
  }

  async getUser(userName) {
    return this.get(`${config.USERS_API_URL}/${userName}`);
  }

  async createUser(userData) {
    return this.post(config.USERS_API_URL, userData);
  }

  async updateUser(userName, userData) {
    return this.put(`${config.USERS_API_URL}/${userName}`, userData);
  }

  async deleteUser(userName) {
    return this.delete(`${config.USERS_API_URL}/${userName}`);
  }
}

/**
 * Log response details for debugging failed tests
 */
function logResponse(response, context = '') {
  const prefix = context ? `[${context}] ` : '';
  console.log(`${prefix}Status: ${response.status}`);
  console.log(`${prefix}URL: ${response.config?.url || 'unknown'}`);
  console.log(`${prefix}Method: ${response.config?.method?.toUpperCase() || 'unknown'}`);
  if (response.data) {
    const dataStr = typeof response.data === 'string'
      ? response.data.substring(0, 1000)
      : JSON.stringify(response.data, null, 2).substring(0, 1000);
    console.log(`${prefix}Response:`, dataStr);
  }
}

// Export singleton instance and helper
const apiClient = new ApiClient();
apiClient.logResponse = logResponse;
module.exports = apiClient;
