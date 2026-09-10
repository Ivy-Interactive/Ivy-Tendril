export interface OpenFileMessage {
  type: 'openFile';
  path: string;
  line?: number;
  column?: number;
}

export interface OpenWorktreeMessage {
  type: 'openWorktree';
  path: string;
}

export interface OpenDiffMessage {
  type: 'openDiff';
  leftPath: string;
  rightPath: string;
  title?: string;
}

export type WebToHostMessage =
  | OpenFileMessage
  | OpenWorktreeMessage
  | OpenDiffMessage;

export interface ThemeSyncMessage {
  type: 'themeChanged';
  theme: 'dark' | 'light' | 'hc';
}

export type HostToWebMessage = ThemeSyncMessage;

export function validateBridgeMessage(raw: unknown): WebToHostMessage {
  if (!raw || typeof raw !== 'object') {
    throw new Error('Bridge message must be a valid JSON object');
  }

  const obj = raw as Record<string, unknown>;
  if (typeof obj.type !== 'string') {
    throw new Error('Bridge message missing string property "type"');
  }

  switch (obj.type) {
    case 'openFile': {
      if (typeof obj.path !== 'string' || obj.path.trim().length === 0) {
        throw new Error('openFile message requires a non-empty string "path"');
      }
      return {
        type: 'openFile',
        path: obj.path,
        line: typeof obj.line === 'number' && Number.isFinite(obj.line) ? obj.line : undefined,
        column: typeof obj.column === 'number' && Number.isFinite(obj.column) ? obj.column : undefined
      };
    }

    case 'openWorktree': {
      if (typeof obj.path !== 'string' || obj.path.trim().length === 0) {
        throw new Error('openWorktree message requires a non-empty string "path"');
      }
      return {
        type: 'openWorktree',
        path: obj.path
      };
    }

    case 'openDiff': {
      if (typeof obj.leftPath !== 'string' || obj.leftPath.trim().length === 0) {
        throw new Error('openDiff message requires a non-empty string "leftPath"');
      }
      if (typeof obj.rightPath !== 'string' || obj.rightPath.trim().length === 0) {
        throw new Error('openDiff message requires a non-empty string "rightPath"');
      }
      return {
        type: 'openDiff',
        leftPath: obj.leftPath,
        rightPath: obj.rightPath,
        title: typeof obj.title === 'string' && obj.title.trim().length > 0 ? obj.title : undefined
      };
    }

    default:
      throw new Error(`Unsupported bridge message type: "${String(obj.type)}"`);
  }
}

export function mapColorThemeKindToTheme(kind: number): 'dark' | 'light' | 'hc' {
  switch (kind) {
    case 1: // ColorThemeKind.Light
    case 4: // ColorThemeKind.HighContrastLight
      return 'light';
    case 3: // ColorThemeKind.HighContrast
      return 'hc';
    case 2: // ColorThemeKind.Dark
    default:
      return 'dark';
  }
}
