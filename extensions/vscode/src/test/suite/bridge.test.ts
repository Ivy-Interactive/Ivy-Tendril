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

    it('should validate valid executeCommand message', () => {
      const msg = validateBridgeMessage({
        type: 'executeCommand',
        command: 'tendril.createPlan',
        args: ['test-arg']
      });

      assert.strictEqual(msg.type, 'executeCommand');
      if (msg.type === 'executeCommand') {
        assert.strictEqual(msg.command, 'tendril.createPlan');
        assert.deepStrictEqual(msg.args, ['test-arg']);
      }
    });

    it('should validate valid startJob message for all supported types', () => {
      const createMsg = validateBridgeMessage({
        type: 'startJob',
        jobType: 'CreatePlan',
        description: 'New feature plan',
        project: 'ivy-tendril'
      });
      assert.strictEqual(createMsg.type, 'startJob');
      if (createMsg.type === 'startJob') {
        assert.strictEqual(createMsg.jobType, 'CreatePlan');
        assert.strictEqual(createMsg.description, 'New feature plan');
        assert.strictEqual(createMsg.project, 'ivy-tendril');
      }

      const execMsg = validateBridgeMessage({
        type: 'startJob',
        jobType: 'ExecutePlan',
        planId: '00399'
      });
      assert.strictEqual(execMsg.type, 'startJob');
      if (execMsg.type === 'startJob') {
        assert.strictEqual(execMsg.jobType, 'ExecutePlan');
        assert.strictEqual(execMsg.planId, '00399');
      }

      const retryMsg = validateBridgeMessage({
        type: 'startJob',
        jobType: 'RetryPlan',
        planId: '00399',
        changeRequest: 'Fix unit tests'
      });
      assert.strictEqual(retryMsg.type, 'startJob');
      if (retryMsg.type === 'startJob') {
        assert.strictEqual(retryMsg.jobType, 'RetryPlan');
        assert.strictEqual(retryMsg.planId, '00399');
        assert.strictEqual(retryMsg.changeRequest, 'Fix unit tests');
      }

      const updateMsg = validateBridgeMessage({
        type: 'startJob',
        jobType: 'UpdatePlan',
        planId: '00399'
      });
      assert.strictEqual(updateMsg.type, 'startJob');
      if (updateMsg.type === 'startJob') {
        assert.strictEqual(updateMsg.jobType, 'UpdatePlan');
        assert.strictEqual(updateMsg.planId, '00399');
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
      assert.throws(() => validateBridgeMessage({ type: 'executeCommand' }));
      assert.throws(() => validateBridgeMessage({ type: 'executeCommand', command: '' }));
      assert.throws(() => validateBridgeMessage({ type: 'executeCommand', command: 'cmd', args: 'invalid' }));
      assert.throws(() => validateBridgeMessage({ type: 'startJob', jobType: 'InvalidType' }));
      assert.throws(() => validateBridgeMessage({ type: 'startJob', jobType: 'CreatePlan' }));
      assert.throws(() => validateBridgeMessage({ type: 'startJob', jobType: 'ExecutePlan' }));
      assert.throws(() => validateBridgeMessage({ type: 'startJob', jobType: 'RetryPlan', planId: '001' }));
      assert.throws(() => validateBridgeMessage({ type: 'startJob', jobType: 'UpdatePlan' }));
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

    it('should route executeCommand to host.executeCommand', async () => {
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
          return 'command-result' as any;
        }
      };

      const handler = new BridgeHandler(mockHost);
      const result = await handler.handleRawMessage({
        type: 'executeCommand',
        command: 'tendril.createPlan',
        args: ['arg1', 42]
      });

      assert.strictEqual(executedCmd, 'tendril.createPlan');
      assert.deepStrictEqual(executedArgs, ['arg1', 42]);
      assert.strictEqual(result, 'command-result');
    });

    it('should route startJob to host.startJob if host supports it', async () => {
      let startedJobMessage: any = null;

      const mockHost: BridgeHost = {
        openTextDocument: async () => ({} as any),
        showTextDocument: async () => ({} as any),
        getWorkspaceFolders: () => [],
        updateWorkspaceFolders: () => true,
        executeCommand: async () => ({} as any),
        startJob: async (msg: any) => {
          startedJobMessage = msg;
          return { jobId: '09999', status: 'Started' };
        }
      };

      const handler = new BridgeHandler(mockHost);
      const res = (await handler.handleRawMessage({
        type: 'startJob',
        jobType: 'CreatePlan',
        description: 'Test create plan',
        project: 'ivy-tendril'
      })) as any;

      assert.ok(startedJobMessage);
      assert.strictEqual(startedJobMessage.jobType, 'CreatePlan');
      assert.strictEqual(startedJobMessage.description, 'Test create plan');
      assert.strictEqual(startedJobMessage.project, 'ivy-tendril');
      assert.strictEqual(res.jobId, '09999');
    });

    it('should route startJob to jobRunner if jobRunner is provided', async () => {
      let createPlanCalledWith: any = null;
      let executePlanCalledWith: any = null;
      let retryPlanCalledWith: any = null;
      let updatePlanCalledWith: any = null;

      const mockJobRunner: any = {
        startCreatePlan: async (desc: string, proj?: string) => {
          createPlanCalledWith = { desc, proj };
          return { jobId: '01001', status: 'Started', message: 'OK' };
        },
        startExecutePlan: async (planId: string) => {
          executePlanCalledWith = { planId };
          return { jobId: '01002', status: 'Started', message: 'OK' };
        },
        startRetryPlan: async (planId: string, req: string) => {
          retryPlanCalledWith = { planId, req };
          return { jobId: '01003', status: 'Started', message: 'OK' };
        },
        startUpdatePlan: async (planId: string, inst: string) => {
          updatePlanCalledWith = { planId, inst };
          return { jobId: '01004', status: 'Started', message: 'OK' };
        }
      };

      const mockHost: BridgeHost = {
        openTextDocument: async () => ({} as any),
        showTextDocument: async () => ({} as any),
        getWorkspaceFolders: () => [],
        updateWorkspaceFolders: () => true,
        executeCommand: async () => ({} as any)
      };

      const handler = new BridgeHandler(mockHost, mockJobRunner);

      const res1 = (await handler.handleRawMessage({
        type: 'startJob',
        jobType: 'CreatePlan',
        description: 'New task',
        project: 'test-project'
      })) as any;
      assert.deepStrictEqual(createPlanCalledWith, { desc: 'New task', proj: 'test-project' });
      assert.strictEqual(res1.jobId, '01001');

      const res2 = (await handler.handleRawMessage({
        type: 'startJob',
        jobType: 'ExecutePlan',
        planId: '00123'
      })) as any;
      assert.deepStrictEqual(executePlanCalledWith, { planId: '00123' });
      assert.strictEqual(res2.jobId, '01002');

      const res3 = (await handler.handleRawMessage({
        type: 'startJob',
        jobType: 'RetryPlan',
        planId: '00123',
        changeRequest: 'Fix review feedback'
      })) as any;
      assert.deepStrictEqual(retryPlanCalledWith, { planId: '00123', req: 'Fix review feedback' });
      assert.strictEqual(res3.jobId, '01003');

      const res4 = (await handler.handleRawMessage({
        type: 'startJob',
        jobType: 'UpdatePlan',
        planId: '00123',
        description: 'Updated instructions'
      })) as any;
      assert.deepStrictEqual(updatePlanCalledWith, { planId: '00123', inst: 'Updated instructions' });
      assert.strictEqual(res4.jobId, '01004');
    });

    it('should throw if startJob is invoked without host or jobRunner support', async () => {
      const mockHost: BridgeHost = {
        openTextDocument: async () => ({} as any),
        showTextDocument: async () => ({} as any),
        getWorkspaceFolders: () => [],
        updateWorkspaceFolders: () => true,
        executeCommand: async () => ({} as any)
      };

      const handler = new BridgeHandler(mockHost);
      await assert.rejects(async () => {
        await handler.handleRawMessage({
          type: 'startJob',
          jobType: 'ExecutePlan',
          planId: '00123'
        });
      }, /Host does not support startJob/);
    });
  });
});
