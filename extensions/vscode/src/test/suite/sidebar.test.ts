import * as assert from 'assert';
import * as vscode from 'vscode';
import { COMMANDS } from '../../constants';
import { ServerManager } from '../../server/serverManager';
import { ServerHealthInfo } from '../../server/types';
import { SidebarProvider } from '../../views/sidebarProvider';
import { activate, deactivate } from '../../extension';
import { vscodeMock } from '../vscodeMock';

describe('Tendril Sidebar & Browser Action Suite', () => {
  describe('SidebarProvider quick actions', () => {
    it('should include both Open Dashboard and Open in Browser action items', async () => {
      const mockEmitter = new vscode.EventEmitter<ServerHealthInfo>();
      const mockServerManager = {
        onDidChangeState: mockEmitter.event,
        getHealthInfo: async (): Promise<ServerHealthInfo> => ({
          isAlive: true,
          port: 5000,
          pid: 12345,
          baseUrl: 'http://localhost:5000',
          activeJobsCount: 1
        }),
        getRecentPlans: async () => []
      } as unknown as ServerManager;

      const provider = new SidebarProvider(mockServerManager);
      const items = await provider.getChildren();

      const actionItems = items.filter(item => item.itemType === 'action');
      const openDashboardItem = actionItems.find(item => item.label === 'Open Dashboard');
      const openInBrowserItem = actionItems.find(item => item.label === 'Open in Browser');

      assert.ok(openDashboardItem, 'Open Dashboard action item must be present');
      assert.strictEqual(
        openDashboardItem?.command?.command,
        COMMANDS.openDashboard,
        'Open Dashboard must bind to tendril.openDashboard'
      );
      assert.ok(openDashboardItem?.iconPath instanceof vscode.ThemeIcon);
      assert.strictEqual(
        (openDashboardItem?.iconPath as vscode.ThemeIcon).id,
        'browser',
        'Open Dashboard must have browser icon'
      );

      assert.ok(openInBrowserItem, 'Open in Browser action item must be present');
      assert.strictEqual(
        openInBrowserItem?.command?.command,
        COMMANDS.openInBrowser,
        'Open in Browser must bind to tendril.openInBrowser'
      );
      assert.ok(openInBrowserItem?.iconPath instanceof vscode.ThemeIcon);
      assert.strictEqual(
        (openInBrowserItem?.iconPath as vscode.ThemeIcon).id,
        'globe',
        'Open in Browser must have globe icon'
      );
    });

    it('should show both dashboard and browser items even when server is stopped', async () => {
      const mockEmitter = new vscode.EventEmitter<ServerHealthInfo>();
      const mockServerManager = {
        onDidChangeState: mockEmitter.event,
        getHealthInfo: async (): Promise<ServerHealthInfo> => ({
          isAlive: false,
          activeJobsCount: 0
        }),
        getRecentPlans: async () => []
      } as unknown as ServerManager;

      const provider = new SidebarProvider(mockServerManager);
      const items = await provider.getChildren();

      const openDashboardItem = items.find(item => item.label === 'Open Dashboard');
      const openInBrowserItem = items.find(item => item.label === 'Open in Browser');

      assert.ok(openDashboardItem, 'Open Dashboard should still appear when server offline');
      assert.ok(openInBrowserItem, 'Open in Browser should still appear when server offline');
    });
  });

  describe('Command execution', () => {
    let originalEnsure: typeof ServerManager.prototype.ensureServerRunning;

    beforeEach(async () => {
      originalEnsure = ServerManager.prototype.ensureServerRunning;
      vscodeMock.env.lastOpenedUri = undefined;
      vscodeMock.window.lastErrorMessage = undefined;

      const subscriptions: vscode.Disposable[] = [];
      const context = {
        subscriptions,
        extensionUri: vscode.Uri.file('/mock/path')
      } as unknown as vscode.ExtensionContext;

      await activate(context);
    });

    afterEach(() => {
      ServerManager.prototype.ensureServerRunning = originalEnsure;
      deactivate();
    });

    it('should open external browser with baseUrl on openInBrowser', async () => {
      ServerManager.prototype.ensureServerRunning = async () => ({
        port: 4567,
        pid: 9999,
        baseUrl: 'http://localhost:4567',
        scheme: 'http',
        heartbeat: new Date()
      });

      await vscode.commands.executeCommand(COMMANDS.openInBrowser);

      const openedUri = vscodeMock.env.lastOpenedUri as { fsPath?: string } | undefined;
      assert.ok(openedUri, 'External URI must be opened');
      assert.strictEqual(openedUri?.toString(), 'http://localhost:4567');
      assert.strictEqual(vscodeMock.window.lastErrorMessage, undefined);
    });

    it('should show error message if ensureServerRunning throws', async () => {
      ServerManager.prototype.ensureServerRunning = async () => {
        throw new Error('Server failed to start');
      };

      await vscode.commands.executeCommand(COMMANDS.openInBrowser);

      assert.strictEqual(vscodeMock.env.lastOpenedUri, undefined);
      assert.ok(vscodeMock.window.lastErrorMessage?.includes('Failed to open Tendril in browser'));
      assert.ok(vscodeMock.window.lastErrorMessage?.includes('Server failed to start'));
    });
  });
});
