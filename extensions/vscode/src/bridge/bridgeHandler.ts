import * as vscode from 'vscode';
import * as path from 'path';
import {
  OpenDiffMessage,
  OpenFileMessage,
  OpenWorktreeMessage,
  validateBridgeMessage,
  WebToHostMessage
} from './bridgeProtocol';

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
  constructor(private readonly host: BridgeHost = defaultBridgeHost) {}

  public async handleRawMessage(rawMessage: unknown): Promise<void> {
    const message = validateBridgeMessage(rawMessage);
    await this.handleMessage(message);
  }

  public async handleMessage(message: WebToHostMessage): Promise<void> {
    switch (message.type) {
      case 'openFile':
        await this.handleOpenFile(message);
        break;
      case 'openWorktree':
        await this.handleOpenWorktree(message);
        break;
      case 'openDiff':
        await this.handleOpenDiff(message);
        break;
    }
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
