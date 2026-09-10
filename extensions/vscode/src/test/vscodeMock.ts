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
  window: {
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
    showErrorMessage: async () => undefined,
    showTextDocument: async () => ({}),
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
    updateWorkspaceFolders: () => true
  },
  commands: {
    registerCommand: () => ({ dispose: () => {} }),
    executeCommand: async () => undefined
  }
};
