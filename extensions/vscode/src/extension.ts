import * as vscode from 'vscode';
import * as path from 'path';
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
    vscode.commands.registerCommand(COMMANDS.openInBrowser, async () => {
      try {
        const result = await serverManager!.ensureServerRunning();
        await vscode.env.openExternal(vscode.Uri.parse(result.baseUrl));
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to open Tendril in browser: ${msg}`);
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

  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.addCurrentProject, async (folderUri?: vscode.Uri) => {
      try {
        const folders = vscode.workspace.workspaceFolders;
        const targetFolder = folderUri
          ? folders?.find(f => f.uri.toString() === folderUri.toString()) ?? { uri: folderUri, name: path.basename(folderUri.fsPath) }
          : folders?.[0];

        if (!targetFolder) {
          vscode.window.showWarningMessage('No workspace folder open to add to Tendril.');
          return;
        }

        const folderPath = targetFolder.uri.fsPath;
        const folderName = path.basename(folderPath) || targetFolder.name;

        await vscode.window.withProgress(
          {
            location: vscode.ProgressLocation.Notification,
            title: `Adding '${folderName}' to Tendril...`,
            cancellable: false
          },
          async () => {
            await serverManager!.executeCli(['project', 'add', folderName]);
            await serverManager!.executeCli(['project', 'add-repo', folderName, folderPath]);
            await serverManager!.executeCli(['job', 'start', 'AddProject', folderName]);
          }
        );

        vscode.window.showInformationMessage(`Project '${folderName}' added to Tendril. Setup job started.`);
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to add project to Tendril: ${msg}`);
      }
    })
  );

  const promptedWorkspaces = new Set<string>();

  async function checkAndPromptUnmanagedWorkspace(): Promise<void> {
    if (!serverManager) {
      return;
    }

    try {
      const status = await serverManager.checkWorkspaceProjectStatus();
      if (!status.isManaged && status.workspacePath) {
        if (promptedWorkspaces.has(status.workspacePath)) {
          return;
        }
        promptedWorkspaces.add(status.workspacePath);

        const folderName = path.basename(status.workspacePath);
        const action = await vscode.window.showInformationMessage(
          `Workspace folder '${folderName}' is not registered in Tendril. Add it now?`,
          'Add to Tendril'
        );
        if (action === 'Add to Tendril') {
          await vscode.commands.executeCommand(COMMANDS.addCurrentProject);
        }
      }
    } catch {
      // Ignore background checking errors
    }
  }

  context.subscriptions.push(
    vscode.workspace.onDidChangeWorkspaceFolders(() => {
      void checkAndPromptUnmanagedWorkspace();
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

    await checkAndPromptUnmanagedWorkspace();
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
