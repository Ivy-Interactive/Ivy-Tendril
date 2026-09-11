import * as vscode from 'vscode';
import { COMMANDS } from '../constants';
import { ServerHealthInfo } from '../server/types';

export class StatusBarItem implements vscode.Disposable {
  private readonly item: vscode.StatusBarItem;

  constructor() {
    this.item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
    this.item.command = COMMANDS.openDashboard;
    this.item.tooltip = 'Click to open Tendril Dashboard';
    this.update({ isAlive: false, activeJobsCount: 0 });
    this.item.show();
  }

  public update(health: ServerHealthInfo): void {
    if (!health.isAlive) {
      this.item.text = '$(circle-slash) Tendril: Offline';
      this.item.tooltip = 'Tendril server is offline. Click to open dashboard or start server.';
      return;
    }

    if (health.activeJobsCount > 0) {
      this.item.text = `$(sync~spin) Tendril: ${health.activeJobsCount} Jobs Running`;
      this.item.tooltip = `Tendril server active on port ${health.port ?? 'unknown'} with ${health.activeJobsCount} active jobs.`;
    } else {
      this.item.text = '$(symbol-event) Tendril: Ready';
      this.item.tooltip = `Tendril server active on port ${health.port ?? 'unknown'}. Click to open dashboard.`;
    }
  }

  public dispose(): void {
    this.item.dispose();
  }
}
