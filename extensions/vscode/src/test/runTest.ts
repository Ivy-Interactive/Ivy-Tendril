import * as path from 'path';
import { run } from './suite/index';

async function main(): Promise<void> {
  try {
    if (process.env.VSCODE_TEST_ELECTRON === '1') {
      const { runTests } = await import('@vscode/test-electron');
      const extensionDevelopmentPath = path.resolve(__dirname, '../../');
      const extensionTestsPath = path.resolve(__dirname, './suite/index');
      await runTests({ extensionDevelopmentPath, extensionTestsPath });
    } else {
      console.log('Running Tendril extension test suite...');
      await run();
      console.log('All tests passed successfully.');
    }
  } catch (err) {
    console.error('Failed to run tests:', err);
    process.exit(1);
  }
}

void main();
