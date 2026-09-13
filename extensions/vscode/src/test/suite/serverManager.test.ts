import * as assert from 'assert';
import { buildServerArgs } from '../../server/serverManager';

describe('ServerManager Suite', () => {
  describe('buildServerArgs', () => {
    it('should return --find-available-port when port is 0', () => {
      const args = buildServerArgs(0);
      assert.deepStrictEqual(args, ['--web', '--find-available-port']);
    });

    it('should return --find-available-port when port is undefined or omitted', () => {
      const argsOmitted = buildServerArgs();
      assert.deepStrictEqual(argsOmitted, ['--web', '--find-available-port']);

      const argsUndefined = buildServerArgs(undefined);
      assert.deepStrictEqual(argsUndefined, ['--web', '--find-available-port']);
    });

    it('should return --find-available-port when port is negative', () => {
      const args = buildServerArgs(-1);
      assert.deepStrictEqual(args, ['--web', '--find-available-port']);
    });

    it('should return --port flag when port is positive', () => {
      const args = buildServerArgs(5015);
      assert.deepStrictEqual(args, ['--web', '--port=5015']);
    });
  });
});
