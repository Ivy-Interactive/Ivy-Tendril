import * as assert from 'assert';
import * as path from 'path';
import { BridgeHandler, BridgeHost } from '../../bridge/bridgeHandler';
import {
  mapColorThemeKindToTheme,
  validateBridgeMessage
} from '../../bridge/bridgeProtocol';

describe('Tendril IDE Bridge Suite', () => {
  describe('validateBridgeMessage', () => {
    it('should validate valid openFile message', () => {
      const msg = validateBridgeMessage({
        type: 'openFile',
        path: '/path/to/file.ts',
        line: 42,
        column: 10
      });

      assert.strictEqual(msg.type, 'openFile');
      if (msg.type === 'openFile') {
        assert.strictEqual(msg.path, '/path/to/file.ts');
        assert.strictEqual(msg.line, 42);
        assert.strictEqual(msg.column, 10);
      }
    });

    it('should validate valid openWorktree message', () => {
      const msg = validateBridgeMessage({
        type: 'openWorktree',
        path: '/worktree/path'
      });

      assert.strictEqual(msg.type, 'openWorktree');
      if (msg.type === 'openWorktree') {
        assert.strictEqual(msg.path, '/worktree/path');
      }
    });

    it('should validate valid openDiff message', () => {
      const msg = validateBridgeMessage({
        type: 'openDiff',
        leftPath: '/a.ts',
        rightPath: '/b.ts',
        title: 'Compare A with B'
      });

      assert.strictEqual(msg.type, 'openDiff');
      if (msg.type === 'openDiff') {
        assert.strictEqual(msg.leftPath, '/a.ts');
        assert.strictEqual(msg.rightPath, '/b.ts');
        assert.strictEqual(msg.title, 'Compare A with B');
      }
    });

    it('should throw for unknown or malformed messages', () => {
      assert.throws(() => validateBridgeMessage(null));
      assert.throws(() => validateBridgeMessage(123));
      assert.throws(() => validateBridgeMessage({}));
      assert.throws(() => validateBridgeMessage({ type: 'unknownAction' }));
      assert.throws(() => validateBridgeMessage({ type: 'openFile' }));
      assert.throws(() => validateBridgeMessage({ type: 'openFile', path: '   ' }));
      assert.throws(() => validateBridgeMessage({ type: 'openWorktree' }));
      assert.throws(() => validateBridgeMessage({ type: 'openDiff', leftPath: '/a' }));
    });
  });

  describe('mapColorThemeKindToTheme', () => {
    it('should correctly map VS Code color theme kinds to web theme identifiers', () => {
      // Light = 1, Dark = 2, HighContrast = 3, HighContrastLight = 4
      assert.strictEqual(mapColorThemeKindToTheme(1), 'light');
      assert.strictEqual(mapColorThemeKindToTheme(2), 'dark');
      assert.strictEqual(mapColorThemeKindToTheme(3), 'hc');
      assert.strictEqual(mapColorThemeKindToTheme(4), 'light');
      assert.strictEqual(mapColorThemeKindToTheme(99), 'dark'); // fallback default
    });
  });

  describe('BridgeHandler execution', () => {
    it('should route openFile to host openTextDocument and showTextDocument', async () => {
      let openedPath = '';
      let shownOptions: any = null;

      const mockHost: BridgeHost = {
        openTextDocument: async (filePath: string) => {
          openedPath = filePath;
          return { uri: { fsPath: filePath } } as any;
        },
        showTextDocument: async (_doc: any, opts?: any) => {
          shownOptions = opts;
          return {} as any;
        },
        getWorkspaceFolders: () => [],
        updateWorkspaceFolders: () => true,
        executeCommand: async () => ({} as any)
      };

      const handler = new BridgeHandler(mockHost);
      await handler.handleRawMessage({
        type: 'openFile',
        path: '/test/source.cs',
        line: 15,
        column: 5
      });

      assert.strictEqual(openedPath, '/test/source.cs');
      assert.ok(shownOptions);
      assert.ok(shownOptions.selection);
      assert.strictEqual(shownOptions.selection.start.line, 14); // 0-based index
      assert.strictEqual(shownOptions.selection.start.character, 4);
    });

    it('should route openWorktree and avoid duplicates', async () => {
      const addedFolders: any[] = [];
      const existingFolder = {
        uri: { fsPath: '/existing/repo' },
        name: 'repo',
        index: 0
      };

      const mockHost: BridgeHost = {
        openTextDocument: async () => ({} as any),
        showTextDocument: async () => ({} as any),
        getWorkspaceFolders: () => [existingFolder as any],
        updateWorkspaceFolders: (_start, _count, ...toAdd) => {
          addedFolders.push(...toAdd);
          return true;
        },
        executeCommand: async () => ({} as any)
      };

      const handler = new BridgeHandler(mockHost);

      // Try adding folder that is already in workspace
      await handler.handleRawMessage({
        type: 'openWorktree',
        path: '/existing/repo'
      });
      assert.strictEqual(addedFolders.length, 0);

      // Add a new worktree folder
      await handler.handleRawMessage({
        type: 'openWorktree',
        path: '/worktree/00284'
      });
      assert.strictEqual(addedFolders.length, 1);
      assert.strictEqual(
        path.resolve(addedFolders[0].uri.fsPath),
        path.resolve('/worktree/00284')
      );
      assert.strictEqual(addedFolders[0].name, '00284');
    });

    it('should route openDiff to vscode.diff command', async () => {
      let executedCmd = '';
      let executedArgs: any[] = [];

      const mockHost: BridgeHost = {
        openTextDocument: async () => ({} as any),
        showTextDocument: async () => ({} as any),
        getWorkspaceFolders: () => [],
        updateWorkspaceFolders: () => true,
        executeCommand: async (cmd: string, ...args: any[]) => {
          executedCmd = cmd;
          executedArgs = args;
          return {} as any;
        }
      };

      const handler = new BridgeHandler(mockHost);
      await handler.handleRawMessage({
        type: 'openDiff',
        leftPath: '/old/file.txt',
        rightPath: '/new/file.txt',
        title: 'Revision Comparison'
      });

      assert.strictEqual(executedCmd, 'vscode.diff');
      assert.strictEqual(executedArgs.length, 3);
      assert.strictEqual(executedArgs[0].fsPath, path.resolve('/old/file.txt'));
      assert.strictEqual(executedArgs[1].fsPath, path.resolve('/new/file.txt'));
      assert.strictEqual(executedArgs[2], 'Revision Comparison');
    });
  });
});
