module.exports = {
  testEnvironment: 'node',
  testMatch: ['**/suites/**/*.test.js'],
  testTimeout: 30000,
  verbose: true,
  forceExit: true,
  detectOpenHandles: true,
  globalSetup: './setup.js',
  globalTeardown: './teardown.js',
  // Run tests in order (not parallel) to ensure proper sequencing
  maxWorkers: 1
};
