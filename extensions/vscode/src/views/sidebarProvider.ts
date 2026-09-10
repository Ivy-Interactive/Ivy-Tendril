import * as vscode from 'vscode';
import { COMMANDS } from '../constants';
import { ServerManager } from '../server/serverManager';
import { TendrilPlanSummary } from '../server/types';

export type TreeItemType = 'status' | 'action' | 'jobs' | 'plansHeader' | 'plan';

export class TendrilTreeItem extends vscode.TreeItem {
  constructor(
    public readonly itemType: TreeItemType,
    label: string,
    collapsibleState: vscode.TreeItemCollapsibleState = vscode.TreeItemCollapsibleState.None
  ) {
    super(label, collapsibleState);
  }
}

export class SidebarProvider implements vscode.TreeDataProvider<TendrilTreeItem> {
  private readonly _onDidChangeTreeData: vscode.EventEmitter<TendrilTreeItem | undefined | null | void> =
    new vscode.EventEmitter<TendrilTreeItem | undefined | null | void>();
  public readonly onDidChangeTreeData: vscode.Event<TendrilTreeItem | undefined | null | void> =
    this._onDidChangeTreeData.event;

  constructor(private readonly serverManager: ServerManager) {
    this.serverManager.onDidChangeState(() => this.refresh());
  }

  public refresh(): void {
    this._onDidChangeTreeData.fire();
  }

  public getTreeItem(element: TendrilTreeItem): vscode.TreeItem {
    return element;
  }

  public async getChildren(element?: TendrilTreeItem): Promise<TendrilTreeItem[]> {
    if (element) {
      if (element.itemType === 'plansHeader') {
        return this.getPlanItems();
      }
      return [];
    }

    const items: TendrilTreeItem[] = [];
    const health = await this.serverManager.getHealthInfo();

    // 1. Server Status
    const statusItem = new TendrilTreeItem('status', 'Server Status');
    if (health.isAlive) {
      statusItem.label = `Server: Active (Port ${health.port ?? 'unknown'})`;
      statusItem.description = `PID ${health.pid ?? 'unknown'}`;
      statusItem.iconPath = new vscode.ThemeIcon(
        'pass-filled',
        new vscode.ThemeColor('testing.iconPassed')
      );
    } else {
      statusItem.label = 'Server: Stopped';
      statusItem.description = 'Offline';
      statusItem.iconPath = new vscode.ThemeIcon(
        'circle-slash',
        new vscode.ThemeColor('testing.iconFailed')
      );
    }
    items.push(statusItem);

    // 2. Quick Actions
    const openDashboardItem = new TendrilTreeItem('action', 'Open Dashboard');
    openDashboardItem.iconPath = new vscode.ThemeIcon('browser');
    openDashboardItem.command = {
      command: COMMANDS.openDashboard,
      title: 'Open Dashboard'
    };
    items.push(openDashboardItem);

    if (health.isAlive) {
      const stopServerItem = new TendrilTreeItem('action', 'Stop Server');
      stopServerItem.iconPath = new vscode.ThemeIcon('debug-stop');
      stopServerItem.command = {
        command: COMMANDS.stopServer,
        title: 'Stop Server'
      };
      items.push(stopServerItem);

      const restartServerItem = new TendrilTreeItem('action', 'Restart Server');
      restartServerItem.iconPath = new vscode.ThemeIcon('debug-restart');
      restartServerItem.command = {
        command: COMMANDS.restartServer,
        title: 'Restart Server'
      };
      items.push(restartServerItem);
    } else {
      const startServerItem = new TendrilTreeItem('action', 'Start Server');
      startServerItem.iconPath = new vscode.ThemeIcon('play');
      startServerItem.command = {
        command: COMMANDS.startServer,
        title: 'Start Server'
      };
      items.push(startServerItem);
    }

    // 3. Active Jobs Count
    const jobsItem = new TendrilTreeItem('jobs', 'Active Jobs');
    jobsItem.iconPath = new vscode.ThemeIcon('symbol-event');
    jobsItem.description = health.isAlive
      ? `${health.activeJobsCount} running`
      : 'Server offline';
    items.push(jobsItem);

    // 4. Recent Plans Header
    const plansHeader = new TendrilTreeItem(
      'plansHeader',
      'Recent Plans',
      vscode.TreeItemCollapsibleState.Expanded
    );
    plansHeader.iconPath = new vscode.ThemeIcon('list-tree');
    items.push(plansHeader);

    return items;
  }

  private async getPlanItems(): Promise<TendrilTreeItem[]> {
    const plans: TendrilPlanSummary[] = await this.serverManager.getRecentPlans(5);
    if (plans.length === 0) {
      const emptyItem = new TendrilTreeItem('plan', 'No recent plans found');
      emptyItem.description = '';
      return [emptyItem];
    }

    return plans.map(p => {
      const item = new TendrilTreeItem('plan', `${p.id}: ${p.title}`);
      item.description = p.state;
      item.tooltip = `${p.id}: ${p.title} (${p.state})`;
      item.iconPath = this.getPlanIcon(p.state);
      item.command = {
        command: COMMANDS.openDashboard,
        title: 'Open in Dashboard'
      };
      return item;
    });
  }

  private getPlanIcon(state: string): vscode.ThemeIcon {
    switch (state.toLowerCase()) {
      case 'completed':
        return new vscode.ThemeIcon('check', new vscode.ThemeColor('testing.iconPassed'));
      case 'executing':
      case 'creating':
      case 'updating':
        return new vscode.ThemeIcon('sync~spin', new vscode.ThemeColor('charts.blue'));
      case 'failed':
        return new vscode.ThemeIcon('error', new vscode.ThemeColor('testing.iconFailed'));
      case 'review':
        return new vscode.ThemeIcon('eye', new vscode.ThemeColor('charts.yellow'));
      case 'blocked':
        return new vscode.ThemeIcon('lock');
      default:
        return new vscode.ThemeIcon('file-text');
    }
  }
}
