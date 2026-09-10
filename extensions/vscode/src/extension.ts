import * as vscode from 'vscode';
import { BridgeHandler } from './bridge/bridgeHandler';
import { COMMANDS, CONFIG_KEYS, VIEWS } from './constants';
import { ServerManager } from './server/serverManager';
import { StatusBarItem } from './statusbar/statusBarItem';
import { SidebarProvider } from './views/sidebarProvider';
import { DashboardPanel } from './webview/dashboardPanel';

let serverManager: ServerManager | undefined;
let pollTimer: NodeJS.Timeout | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  serverManager = new ServerManager();
  context.subscriptions.push(serverManager);

  const bridgeHandler = new BridgeHandler();
  const statusBar = new StatusBarItem();
  context.subscriptions.push(statusBar);

  const sidebarProvider = new SidebarProvider(serverManager);
  context.subscriptions.push(
    vscode.window.registerTreeDataProvider(VIEWS.quickAccess, sidebarProvider)
  );

  serverManager.onDidChangeState(health => {
    statusBar.update(health);
    sidebarProvider.refresh();
  });

  // Register commands
  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.openDashboard, async () => {
      try {
        const result = await serverManager!.ensureServerRunning();
        await DashboardPanel.createOrShow(
          context.extensionUri,
          result.baseUrl,
          bridgeHandler
        );
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to open Tendril Dashboard: ${msg}`);
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.openWorktree, async (targetPath?: string) => {
      let resolvedPath = targetPath;
      if (!resolvedPath) {
        const uris = await vscode.window.showOpenDialog({
          canSelectFiles: false,
          canSelectFolders: true,
          canSelectMany: false,
          openLabel: 'Open Worktree as Folder'
        });
        if (uris && uris.length > 0) {
          resolvedPath = uris[0].fsPath;
        }
      }

      if (!resolvedPath) {
        return;
      }

      try {
        await bridgeHandler.handleMessage({
          type: 'openWorktree',
          path: resolvedPath
        });
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to open worktree: ${msg}`);
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.startServer, async () => {
      try {
        await serverManager!.startServer();
        vscode.window.showInformationMessage('Tendril server started successfully.');
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to start Tendril server: ${msg}`);
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.stopServer, async () => {
      try {
        await serverManager!.stopServer();
        vscode.window.showInformationMessage('Tendril server stopped.');
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to stop Tendril server: ${msg}`);
      }
    })
  );

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.restartServer, async () => {
      try {
        await serverManager!.restartServer();
        vscode.window.showInformationMessage('Tendril server restarted successfully.');
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to restart Tendril server: ${msg}`);
      }
    })
  );

  // Background initialization & auto-start check
  void (async () => {
    const health = await serverManager!.getHealthInfo();
    statusBar.update(health);

    const config = vscode.workspace.getConfiguration();
    const autoStart = config.get<boolean>(CONFIG_KEYS.serverAutoStart, true);

    if (!health.isAlive && autoStart) {
      try {
        await serverManager!.startServer();
      } catch {
        // Logged inside ServerManager output channel
      }
    }
  })();

  // Periodic polling for health status (every 10s)
  pollTimer = setInterval(() => {
    if (serverManager) {
      serverManager.notifyStateChanged();
    }
  }, 10000);
}

export function deactivate(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = undefined;
  }

  if (serverManager) {
    serverManager.dispose();
    serverManager = undefined;
  }
}
