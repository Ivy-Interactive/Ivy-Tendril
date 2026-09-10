import * as vscode from 'vscode';
import { BridgeHandler } from '../bridge/bridgeHandler';
import { mapColorThemeKindToTheme } from '../bridge/bridgeProtocol';
import { getWebviewContent } from './htmlHelper';

export class DashboardPanel {
  public static currentPanel: DashboardPanel | undefined;
  public static readonly viewType = 'tendril.dashboard';

  private readonly panel: vscode.WebviewPanel;
  private readonly bridgeHandler: BridgeHandler;
  private disposables: vscode.Disposable[] = [];
  private serverUrl: string;

  public static async createOrShow(
    extensionUri: vscode.Uri,
    serverUrl: string,
    bridgeHandler: BridgeHandler
  ): Promise<DashboardPanel> {
    const column = vscode.window.activeTextEditor
      ? vscode.window.activeTextEditor.viewColumn
      : undefined;

    if (DashboardPanel.currentPanel) {
      DashboardPanel.currentPanel.panel.reveal(column);
      await DashboardPanel.currentPanel.update(serverUrl);
      return DashboardPanel.currentPanel;
    }

    const panel = vscode.window.createWebviewPanel(
      DashboardPanel.viewType,
      'Tendril Dashboard',
      column || vscode.ViewColumn.One,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        localResourceRoots: [extensionUri]
      }
    );

    DashboardPanel.currentPanel = new DashboardPanel(panel, serverUrl, bridgeHandler);
    await DashboardPanel.currentPanel.update(serverUrl);
    return DashboardPanel.currentPanel;
  }

  private constructor(
    panel: vscode.WebviewPanel,
    serverUrl: string,
    bridgeHandler: BridgeHandler
  ) {
    this.panel = panel;
    this.serverUrl = serverUrl;
    this.bridgeHandler = bridgeHandler;

    this.panel.onDidDispose(() => this.dispose(), null, this.disposables);

    this.panel.webview.onDidReceiveMessage(
      async message => {
        try {
          await this.bridgeHandler.handleRawMessage(message);
        } catch (err: unknown) {
          const msg = err instanceof Error ? err.message : String(err);
          vscode.window.showErrorMessage(`Tendril Bridge Error: ${msg}`);
        }
      },
      null,
      this.disposables
    );

    vscode.window.onDidChangeActiveColorTheme(
      theme => {
        const themeKind = mapColorThemeKindToTheme(theme.kind);
        this.panel.webview.postMessage({ type: 'themeChanged', theme: themeKind });
      },
      null,
      this.disposables
    );
  }

  public async update(serverUrl: string): Promise<void> {
    this.serverUrl = serverUrl;
    const uri = vscode.Uri.parse(this.serverUrl);
    const externalUri = await vscode.env.asExternalUri(uri);
    const initialTheme = mapColorThemeKindToTheme(vscode.window.activeColorTheme.kind);

    this.panel.webview.html = getWebviewContent(externalUri.toString(), initialTheme);
  }

  public dispose(): void {
    DashboardPanel.currentPanel = undefined;

    this.panel.dispose();

    while (this.disposables.length) {
      const x = this.disposables.pop();
      if (x) {
        x.dispose();
      }
    }
  }
}
