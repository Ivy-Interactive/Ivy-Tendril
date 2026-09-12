import * as vscode from 'vscode';
import * as path from 'path';
import {
  ExecuteCommandMessage,
  OpenDiffMessage,
  OpenFileMessage,
  OpenWorktreeMessage,
  StartJobMessage,
  validateBridgeMessage,
  WebToHostMessage
} from './bridgeProtocol';
import { IJobRunner } from '../jobs/jobRunner';

export interface BridgeHost {
  openTextDocument(path: string): Thenable<vscode.TextDocument>;
  showTextDocument(
    document: vscode.TextDocument,
    options?: vscode.TextDocumentShowOptions
  ): Thenable<vscode.TextEditor>;
  getWorkspaceFolders(): readonly vscode.WorkspaceFolder[] | undefined;
  updateWorkspaceFolders(
    start: number,
    deleteCount: number | undefined,
    ...workspaceFoldersToAdd: { uri: vscode.Uri; name?: string }[]
  ): boolean;
  executeCommand<T>(command: string, ...rest: unknown[]): Thenable<T>;
  startJob?(message: StartJobMessage): Promise<unknown>;
}

export const defaultBridgeHost: BridgeHost = {
  openTextDocument: (filePath: string) => vscode.workspace.openTextDocument(filePath),
  showTextDocument: (doc: vscode.TextDocument, opts?: vscode.TextDocumentShowOptions) =>
    vscode.window.showTextDocument(doc, opts),
  getWorkspaceFolders: () => vscode.workspace.workspaceFolders,
  updateWorkspaceFolders: (start, deleteCount, ...toAdd) =>
    vscode.workspace.updateWorkspaceFolders(start, deleteCount, ...toAdd),
  executeCommand: (cmd: string, ...rest: unknown[]) => vscode.commands.executeCommand(cmd, ...rest)
};

export class BridgeHandler {
  constructor(
    private readonly host: BridgeHost = defaultBridgeHost,
    private readonly jobRunner?: IJobRunner
  ) {}

  public async handleRawMessage(rawMessage: unknown): Promise<unknown> {
    const message = validateBridgeMessage(rawMessage);
    return await this.handleMessage(message);
  }

  public async handleMessage(message: WebToHostMessage): Promise<unknown> {
    switch (message.type) {
      case 'openFile':
        await this.handleOpenFile(message);
        return undefined;
      case 'openWorktree':
        await this.handleOpenWorktree(message);
        return undefined;
      case 'openDiff':
        await this.handleOpenDiff(message);
        return undefined;
      case 'executeCommand':
        return await this.handleExecuteCommand(message);
      case 'startJob':
        return await this.handleStartJob(message);
    }
  }

  private async handleExecuteCommand(msg: ExecuteCommandMessage): Promise<unknown> {
    const args = msg.args ?? [];
    return await this.host.executeCommand(msg.command, ...args);
  }

  private async handleStartJob(msg: StartJobMessage): Promise<unknown> {
    if (this.host.startJob) {
      return await this.host.startJob(msg);
    }
    if (this.jobRunner) {
      switch (msg.jobType) {
        case 'CreatePlan':
          return await this.jobRunner.startCreatePlan(msg.description!, msg.project);
        case 'ExecutePlan':
          return await this.jobRunner.startExecutePlan(msg.planId!);
        case 'RetryPlan':
          return await this.jobRunner.startRetryPlan(msg.planId!, msg.changeRequest!);
        case 'UpdatePlan':
          return await this.jobRunner.startUpdatePlan(msg.planId!, msg.description ?? '');
      }
    }
    throw new Error('Host does not support startJob');
  }

  private async handleOpenFile(msg: OpenFileMessage): Promise<void> {
    const doc = await this.host.openTextDocument(msg.path);
    let options: vscode.TextDocumentShowOptions | undefined;

    if (typeof msg.line === 'number') {
      const lineIndex = Math.max(0, msg.line - 1);
      const columnIndex = typeof msg.column === 'number' ? Math.max(0, msg.column - 1) : 0;
      const pos = new vscode.Position(lineIndex, columnIndex);
      options = {
        selection: new vscode.Range(pos, pos),
        preserveFocus: false
      };
    }

    await this.host.showTextDocument(doc, options);
  }

  private async handleOpenWorktree(msg: OpenWorktreeMessage): Promise<void> {
    const targetUri = vscode.Uri.file(msg.path);
    const existing = this.host.getWorkspaceFolders() ?? [];
    const normalizedTarget = path.resolve(targetUri.fsPath).toLowerCase();

    const alreadyPresent = existing.some(
      f => path.resolve(f.uri.fsPath).toLowerCase() === normalizedTarget
    );

    if (!alreadyPresent) {
      const folderName = path.basename(msg.path);
      this.host.updateWorkspaceFolders(existing.length, 0, {
        uri: targetUri,
        name: folderName
      });
    }
  }

  private async handleOpenDiff(msg: OpenDiffMessage): Promise<void> {
    const leftUri = vscode.Uri.file(msg.leftPath);
    const rightUri = vscode.Uri.file(msg.rightPath);
    const title =
      msg.title ||
      `${path.basename(msg.leftPath)} <-> ${path.basename(msg.rightPath)}`;

    await this.host.executeCommand('vscode.diff', leftUri, rightUri, title);
  }
}
