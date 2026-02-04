const Sequencer = require('@jest/test-sequencer').default;

/**
 * Custom test sequencer to ensure tests run in alphabetical order.
 * This ensures tenant creation (03-tenant) runs before dependent tests.
 */
class AlphabeticalSequencer extends Sequencer {
  sort(tests) {
    return [...tests].sort((a, b) => a.path.localeCompare(b.path));
  }
}

module.exports = AlphabeticalSequencer;
