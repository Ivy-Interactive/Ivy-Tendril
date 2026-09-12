import * as path from 'path';

export class MockUri {
  public readonly scheme: string;
  public readonly fsPath: string;
  public readonly path: string;

  constructor(fsPath: string) {
    this.scheme = 'file';
    this.fsPath = fsPath;
    this.path = fsPath;
  }

  public static file(filePath: string): MockUri {
    return new MockUri(filePath);
  }

  public static parse(val: string): MockUri {
    return new MockUri(val);
  }

  public static joinPath(base: MockUri, ...pathSegments: string[]): MockUri {
    return new MockUri(path.join(base.fsPath, ...pathSegments));
  }

  public toString(): string {
    return this.fsPath;
  }
}

export class MockPosition {
  constructor(public readonly line: number, public readonly character: number) {}
}

export class MockRange {
  public readonly start: MockPosition;
  public readonly end: MockPosition;

  constructor(start: MockPosition, end: MockPosition) {
    this.start = start;
    this.end = end;
  }
}

export enum ColorThemeKind {
  Light = 1,
  Dark = 2,
  HighContrast = 3,
  HighContrastLight = 4
}

export enum TreeItemCollapsibleState {
  None = 0,
  Collapsed = 1,
  Expanded = 2
}

export enum StatusBarAlignment {
  Left = 1,
  Right = 2
}

export class MockThemeIcon {
  constructor(public readonly id: string, public readonly color?: unknown) {}
}

export class MockThemeColor {
  constructor(public readonly id: string) {}
}

export class MockTreeItem {
  public iconPath?: unknown;
  public command?: { command: string; title: string; arguments?: unknown[] };
  public description?: string | boolean;
  public tooltip?: string | unknown;

  constructor(
    public label: string,
    public collapsibleState: TreeItemCollapsibleState = TreeItemCollapsibleState.None
  ) {}
}

export class MockEventEmitter<T = unknown> {
  private listeners: ((e: T) => void)[] = [];

  public event = (listener: (e: T) => void) => {
    this.listeners.push(listener);
    return {
      dispose: () => {
        const idx = this.listeners.indexOf(listener);
        if (idx >= 0) {
          this.listeners.splice(idx, 1);
        }
      }
    };
  };

  public fire(data: T): void {
    for (const listener of this.listeners) {
      listener(data);
    }
  }

  public dispose(): void {
    this.listeners = [];
  }
}

export class MockCancellationToken {
  public isCancellationRequested = false;
  private listeners: (() => void)[] = [];

  public onCancellationRequested = (listener: () => void) => {
    this.listeners.push(listener);
    return {
      dispose: () => {
        const idx = this.listeners.indexOf(listener);
        if (idx >= 0) {
          this.listeners.splice(idx, 1);
        }
      }
    };
  };

  public cancel(): void {
    this.isCancellationRequested = true;
    for (const listener of this.listeners) {
      listener();
    }
  }
}

export class MockCancellationTokenSource {
  public token = new MockCancellationToken();

  public cancel(): void {
    this.token.cancel();
  }

  public dispose(): void {}
}

const registeredCommands = new Map<string, (...args: unknown[]) => unknown>();

export const vscodeMock = {
  Uri: MockUri,
  Position: MockPosition,
  Range: MockRange,
  ColorThemeKind,
  TreeItemCollapsibleState,
  StatusBarAlignment,
  ThemeIcon: MockThemeIcon,
  ThemeColor: MockThemeColor,
  TreeItem: MockTreeItem,
  EventEmitter: MockEventEmitter,
  CancellationTokenSource: MockCancellationTokenSource,
  env: {
    lastOpenedUri: undefined as unknown,
    openExternal: async (uri: unknown) => {
      vscodeMock.env.lastOpenedUri = uri;
      return true;
    },
    asExternalUri: async (uri: unknown) => uri
  },
  window: {
    lastErrorMessage: undefined as string | undefined,
    createOutputChannel: (name: string) => ({
      name,
      append: () => {},
      appendLine: () => {},
      show: () => {},
      dispose: () => {}
    }),
    createStatusBarItem: () => ({
      text: '',
      tooltip: '',
      command: '',
      show: () => {},
      dispose: () => {}
    }),
    showInformationMessage: async () => undefined,
    showWarningMessage: async () => undefined,
    showErrorMessage: async (msg: string) => {
      vscodeMock.window.lastErrorMessage = msg;
      return undefined;
    },
    showTextDocument: async () => ({}),
    showInputBox: async () => undefined,
    showQuickPick: async () => undefined,
    activeColorTheme: { kind: ColorThemeKind.Dark },
    onDidChangeActiveColorTheme: () => ({ dispose: () => {} }),
    registerTreeDataProvider: () => ({ dispose: () => {} })
  },
  workspace: {
    getConfiguration: () => ({
      get: (_key: string, defaultVal: unknown) => defaultVal
    }),
    openTextDocument: async (filePath: string) => ({ uri: MockUri.file(filePath) }),
    workspaceFolders: [],
    updateWorkspaceFolders: () => true,
    onDidChangeWorkspaceFolders: () => ({ dispose: () => {} })
  },
  commands: {
    registerCommand: (command: string, callback: (...args: unknown[]) => unknown) => {
      registeredCommands.set(command, callback);
      return {
        dispose: () => {
          registeredCommands.delete(command);
        }
      };
    },
    executeCommand: async (command: string, ...args: unknown[]) => {
      const handler = registeredCommands.get(command);
      if (handler) {
        return await handler(...args);
      }
      return undefined;
    }
  },
  chat: {
    createChatParticipant: (id: string, handler: unknown) => ({
      id,
      requestHandler: handler,
      iconPath: undefined,
      dispose: () => {}
    })
  }
};
