import * as path from 'path';
import Mocha from 'mocha';
import { glob } from 'glob';
import { vscodeMock } from '../vscodeMock';

// If running in standalone Node environment without VS Code Extension Host:
try {
  require.resolve('vscode');
} catch {
  // eslint-disable-next-line @typescript-eslint/no-var-requires
  const Module = require('module');
  const originalLoad = Module._load;
  Module._load = function (request: string, parent: unknown, isMain: boolean) {
    if (request === 'vscode') {
      return vscodeMock;
    }
    return originalLoad.apply(this, [request, parent, isMain]);
  };
}

export async function run(): Promise<void> {
  const mocha = new Mocha({
    ui: 'bdd',
    color: true,
    timeout: 10000
  });

  const testsRoot = path.resolve(__dirname, '.');

  const files = await glob('**/**.test.js', { cwd: testsRoot });
  for (const file of files) {
    mocha.addFile(path.resolve(testsRoot, file));
  }

  return new Promise((resolve, reject) => {
    try {
      mocha.run(failures => {
        if (failures > 0) {
          reject(new Error(`${failures} tests failed.`));
        } else {
          resolve();
        }
      });
    } catch (err) {
      reject(err);
    }
  });
}
