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

export interface ExecuteCommandMessage {
  type: 'executeCommand';
  command: string;
  args?: unknown[];
}

export interface StartJobMessage {
  type: 'startJob';
  jobType: 'CreatePlan' | 'ExecutePlan' | 'RetryPlan' | 'UpdatePlan';
  planId?: string;
  description?: string;
  project?: string;
  changeRequest?: string;
}

export type WebToHostMessage =
  | OpenFileMessage
  | OpenWorktreeMessage
  | OpenDiffMessage
  | ExecuteCommandMessage
  | StartJobMessage;

export interface ThemeSyncMessage {
  type: 'themeChanged';
  theme: 'dark' | 'light' | 'hc';
}

export interface JobStatusUpdateMessage {
  type: 'jobStatus';
  jobId: string;
  status: string;
  message?: string;
  planId?: string;
}

export type HostToWebMessage = ThemeSyncMessage | JobStatusUpdateMessage;

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

    case 'executeCommand': {
      if (typeof obj.command !== 'string' || obj.command.trim().length === 0) {
        throw new Error('executeCommand message requires a non-empty string "command"');
      }
      if (obj.args !== undefined && !Array.isArray(obj.args)) {
        throw new Error('executeCommand message "args" must be an array if provided');
      }
      return {
        type: 'executeCommand',
        command: obj.command,
        args: obj.args as unknown[] | undefined
      };
    }

    case 'startJob': {
      const validJobTypes = ['CreatePlan', 'ExecutePlan', 'RetryPlan', 'UpdatePlan'];
      if (typeof obj.jobType !== 'string' || !validJobTypes.includes(obj.jobType)) {
        throw new Error(`startJob message requires a valid "jobType" (${validJobTypes.join(', ')})`);
      }
      if (obj.jobType === 'CreatePlan') {
        if (typeof obj.description !== 'string' || obj.description.trim().length === 0) {
          throw new Error('startJob CreatePlan requires a non-empty string "description"');
        }
      }
      if (obj.jobType === 'ExecutePlan' || obj.jobType === 'RetryPlan' || obj.jobType === 'UpdatePlan') {
        if (typeof obj.planId !== 'string' || obj.planId.trim().length === 0) {
          throw new Error(`startJob ${obj.jobType} requires a non-empty string "planId"`);
        }
      }
      if (obj.jobType === 'RetryPlan') {
        if (typeof obj.changeRequest !== 'string' || obj.changeRequest.trim().length === 0) {
          throw new Error('startJob RetryPlan requires a non-empty string "changeRequest"');
        }
      }
      return {
        type: 'startJob',
        jobType: obj.jobType as 'CreatePlan' | 'ExecutePlan' | 'RetryPlan' | 'UpdatePlan',
        planId: typeof obj.planId === 'string' ? obj.planId : undefined,
        description: typeof obj.description === 'string' ? obj.description : undefined,
        project: typeof obj.project === 'string' ? obj.project : undefined,
        changeRequest: typeof obj.changeRequest === 'string' ? obj.changeRequest : undefined
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
