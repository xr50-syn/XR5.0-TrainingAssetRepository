const config = require('../config');

/**
 * Test Data Generators
 *
 * Provides consistent test data for functional tests.
 * All generated data includes timestamps to ensure uniqueness.
 */

const timestamp = Date.now();

// Storage type can be overridden via STORAGE_TYPE env var
// Defaults to 'S3' but can be set to 'OwnCloud'
const STORAGE_TYPE = process.env.STORAGE_TYPE || 'S3';

/**
 * Generate a test tenant configuration with S3 storage
 */
function createS3Tenant(name = config.TEST_TENANT) {
  return {
    tenantName: name,
    tenantGroup: 'verification-tests',
    description: `Verification test tenant created at ${new Date().toISOString()}`,
    storageType: 'S3',
    s3Config: {
      bucketName: config.S3_BUCKET,
      bucketRegion: config.S3_REGION,
      ...(config.S3_ENDPOINT && { endpoint: config.S3_ENDPOINT })
    },
    owner: {
      userName: 'testadmin',
      fullName: 'Test Administrator',
      userEmail: `admin@${name}.test`,
      password: 'TestPass123!',
      admin: true
    }
  };
}

/**
 * Generate a test tenant configuration with OwnCloud storage
 */
function createOwnCloudTenant(name = config.TEST_TENANT) {
  return {
    tenantName: name,
    tenantGroup: 'verification-tests',
    description: `Verification test tenant created at ${new Date().toISOString()}`,
    storageType: 'OwnCloud',
    ownCloudConfig: {
      tenantDirectory: `${name}-files`,
      endpoint: process.env.OWNCLOUD_URL || 'http://owncloud:8080'
    },
    owner: {
      userName: 'testadmin',
      fullName: 'Test Administrator',
      userEmail: `admin@${name}.test`,
      password: 'TestPass123!',
      admin: true
    }
  };
}

/**
 * Generate a test tenant configuration based on STORAGE_TYPE environment variable.
 * Defaults to S3 if not set.
 */
function createTenant(name = config.TEST_TENANT) {
  if (STORAGE_TYPE.toLowerCase() === 'owncloud') {
    return createOwnCloudTenant(name);
  }
  return createS3Tenant(name);
}

/**
 * Generate a test tenant with MinIO (S3-compatible) storage
 */
function createMinioTenant(name = config.TEST_TENANT) {
  return {
    tenantName: name,
    tenantGroup: 'verification-tests',
    description: `MinIO test tenant created at ${new Date().toISOString()}`,
    storageType: 'S3',
    s3Config: {
      bucketName: config.S3_BUCKET || 'xr50-test',
      bucketRegion: 'us-east-1',
      endpoint: config.S3_ENDPOINT || 'http://minio:9000',
      forcePathStyle: true
    },
    owner: {
      userName: 'testadmin',
      fullName: 'Test Administrator',
      userEmail: `admin@${name}.test`,
      password: 'TestPass123!',
      admin: true
    }
  };
}

/**
 * Generate a simple material
 */
function createSimpleMaterial(suffix = '') {
  return {
    name: `Test Material ${suffix || timestamp}`,
    description: 'A simple test material for verification',
    type: 'Simple'
  };
}

/**
 * Generate a video material
 */
function createVideoMaterial(suffix = '') {
  return {
    name: `Test Video ${suffix || timestamp}`,
    description: 'A test video material',
    type: 'Video',
    videoPath: '/videos/test-video.mp4',
    videoDuration: 120,
    videoResolution: '1920x1080'
  };
}

/**
 * Generate a video material with timestamps
 */
function createVideoWithTimestamps(suffix = '') {
  return {
    name: `Test Video With Timestamps ${suffix || timestamp}`,
    description: 'A test video material with chapters',
    type: 'Video',
    videoPath: '/videos/test-video-chapters.mp4',
    videoDuration: 300,
    videoResolution: '1920x1080',
    timestamps: [
      {
        title: 'Introduction',
        time: '00:00:00',
        description: 'Overview of the topic'
      },
      {
        title: 'Main Content',
        time: '00:01:30',
        description: 'Detailed explanation'
      },
      {
        title: 'Summary',
        time: '00:04:00',
        description: 'Key takeaways'
      }
    ]
  };
}

/**
 * Generate a checklist material
 */
function createChecklistMaterial(suffix = '') {
  return {
    name: `Test Checklist ${suffix || timestamp}`,
    description: 'A test checklist material',
    type: 'Checklist',
    config: {
      entries: [
        {
          text: 'Step 1: Preparation',
          description: 'Prepare the workspace',
          related: []
        },
        {
          text: 'Step 2: Execution',
          description: 'Execute the main task',
          related: []
        },
        {
          text: 'Step 3: Verification',
          description: 'Verify the results',
          related: []
        }
      ]
    }
  };
}

/**
 * Generate a workflow material
 */
function createWorkflowMaterial(suffix = '') {
  return {
    name: `Test Workflow ${suffix || timestamp}`,
    description: 'A test workflow material',
    type: 'Workflow',
    config: {
      steps: [
        {
          stepNumber: 1,
          title: 'Initialize',
          description: 'Set up the environment',
          instructions: 'Follow the setup guide'
        },
        {
          stepNumber: 2,
          title: 'Process',
          description: 'Execute the main workflow',
          instructions: 'Complete all required steps'
        },
        {
          stepNumber: 3,
          title: 'Finalize',
          description: 'Clean up and verify',
          instructions: 'Ensure all steps completed'
        }
      ]
    }
  };
}

/**
 * Generate a composite material (parent)
 */
function createCompositeMaterial(suffix = '') {
  return {
    name: `Test Composite ${suffix || timestamp}`,
    description: 'A composite material that can contain children',
    type: 'Composite'
  };
}

/**
 * Generate a chatbot material
 */
function createChatbotMaterial(suffix = '', endpoint = 'https://test.xr50.work') {
  return {
    name: `Test Chatbot ${suffix || timestamp}`,
    description: 'A test chatbot material for AI conversations',
    type: 'Chatbot',
    chatbotConfig: endpoint,
    chatbotModel: 'default',
    chatbotPrompt: 'You are a helpful assistant for testing purposes.'
  };
}

/**
 * Generate a training program
 */
function createTrainingProgram(suffix = '') {
  return {
    name: `Test Program ${suffix || timestamp}`,
    description: 'A test training program',
    objectives: 'Verify system functionality',
    requirements: 'None',
    min_level_rank: 1,
    max_level_rank: 5
  };
}

/**
 * Generate a training program with learning paths
 */
function createProgramWithPaths(suffix = '') {
  return {
    name: `Test Program With Paths ${suffix || timestamp}`,
    description: 'A test training program with learning paths',
    objectives: 'Complete verification testing',
    requirements: 'None',
    min_level_rank: 1,
    max_level_rank: 10,
    learning_path: [
      {
        name: 'Beginner Path',
        description: 'Introduction to the topic',
        inherit_from_program: true,
        min_level_rank: 1
      },
      {
        name: 'Advanced Path',
        description: 'Advanced concepts',
        inherit_from_program: true,
        min_level_rank: 5
      }
    ]
  };
}

/**
 * Generate a test user
 */
function createTestUser(suffix = '') {
  const id = suffix || timestamp;
  return {
    userName: `testuser${id}`,
    fullName: `Test User ${id}`,
    userEmail: `testuser${id}@test.local`,
    password: 'TestPass123!',
    admin: false
  };
}

/**
 * Generate a test admin user
 */
function createAdminUser(suffix = '') {
  const id = suffix || timestamp;
  return {
    userName: `admin${id}`,
    fullName: `Admin User ${id}`,
    userEmail: `admin${id}@test.local`,
    password: 'AdminPass123!',
    admin: true
  };
}

/**
 * Generate test file buffer (simple text file)
 */
function createTestTextFile(content = 'Test file content for verification') {
  return {
    buffer: Buffer.from(content, 'utf-8'),
    filename: `test-file-${timestamp}.txt`,
    mimeType: 'text/plain'
  };
}

/**
 * Generate test image buffer (1x1 PNG)
 */
function createTestImageFile() {
  // Minimal valid PNG (1x1 transparent pixel)
  const pngBuffer = Buffer.from([
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
    0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
    0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
    0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, // IDAT chunk
    0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
    0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
    0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, // IEND chunk
    0x42, 0x60, 0x82
  ]);

  return {
    buffer: pngBuffer,
    filename: `test-image-${timestamp}.png`,
    mimeType: 'image/png'
  };
}

/**
 * Track created resources for cleanup
 */
class TestResourceTracker {
  constructor() {
    this.tenants = [];
    this.materials = [];
    this.assets = [];
    this.programs = [];
    this.users = [];
  }

  trackTenant(name) {
    this.tenants.push(name);
  }

  trackMaterial(id) {
    this.materials.push(id);
  }

  trackAsset(id) {
    this.assets.push(id);
  }

  trackProgram(id) {
    this.programs.push(id);
  }

  trackUser(name) {
    this.users.push(name);
  }

  getAll() {
    return {
      tenants: [...this.tenants],
      materials: [...this.materials],
      assets: [...this.assets],
      programs: [...this.programs],
      users: [...this.users]
    };
  }

  clear() {
    this.tenants = [];
    this.materials = [];
    this.assets = [];
    this.programs = [];
    this.users = [];
  }
}

module.exports = {
  createTenant,
  createS3Tenant,
  createOwnCloudTenant,
  createMinioTenant,
  createSimpleMaterial,
  createVideoMaterial,
  createVideoWithTimestamps,
  createChecklistMaterial,
  createWorkflowMaterial,
  createCompositeMaterial,
  createChatbotMaterial,
  createTrainingProgram,
  createProgramWithPaths,
  createTestUser,
  createAdminUser,
  createTestTextFile,
  createTestImageFile,
  TestResourceTracker,
  STORAGE_TYPE
};
