export type EventHandler = (eventName: string, widgetId: string, args: unknown[]) => void;

// ─── EventWire types (matches AgentEventSchema.cs snake_case serialization) ───

export interface SessionInitWire {
  kind: "session_init";
  timestamp: string;
  session_id: string;
  model?: string;
  tools?: string[];
}

export interface TextWire {
  kind: "text";
  timestamp: string;
  text: string;
  delta: boolean;
}

export interface ThinkingWire {
  kind: "thinking";
  timestamp: string;
  content: string;
}

export interface ToolCallWire {
  kind: "tool_call";
  timestamp: string;
  tool_use_id: string;
  tool_name: string;
  description?: string;
  input?: Record<string, unknown>;
}

export interface ToolResultWire {
  kind: "tool_result";
  timestamp: string;
  tool_use_id: string;
  tool_name?: string;
  output?: string;
  is_error: boolean;
}

export interface ResultWire {
  kind: "result";
  timestamp: string;
  response?: string;
  is_success: boolean;
  duration_ms?: number;
  turn_count?: number;
  exit_code?: number;
  usage?: UsageWire;
  permission_denials?: PermissionDenialWire[];
}

export interface UsageWire {
  input_tokens: number;
  output_tokens: number;
  cache_read_tokens: number;
  cache_write_tokens: number;
  reasoning_tokens: number;
  cost_usd?: number;
  premium_requests?: number;
  model?: string;
  model_breakdown?: UsageWire[];
}

export interface ErrorWire {
  kind: "error";
  timestamp: string;
  message: string;
  code?: string;
  is_retryable: boolean;
  is_auth_error: boolean;
}

export interface FileChangeWire {
  kind: "file_change";
  timestamp: string;
  file_path: string;
  change_kind: string;
  lines_added: number;
  lines_removed: number;
}

export interface PermissionRequestWire {
  kind: "permission_request";
  timestamp: string;
  request_id: string;
  tool_name: string;
  description?: string;
  input?: string;
  is_destructive: boolean;
  pattern?: string;
}

export interface PermissionDenialWire {
  kind: "permission_denial";
  timestamp: string;
  tool_name: string;
  input_summary?: string;
}

export interface UserQuestionWire {
  kind: "user_question";
  timestamp: string;
  question_id: string;
  question: string;
  options?: { label: string; value: string; description?: string }[];
  multi_select: boolean;
  description?: string;
  is_blocking: boolean;
  timeout_ms?: number;
}

export type EventWire =
  | SessionInitWire
  | TextWire
  | ThinkingWire
  | ToolCallWire
  | ToolResultWire
  | ResultWire
  | ErrorWire
  | FileChangeWire
  | PermissionRequestWire
  | PermissionDenialWire
  | UserQuestionWire;

// ─── Presentation model (what components render) ──────────────────────────────

export interface ToolUsePresentation {
  toolUseId: string;
  name: string;
  description?: string;
  input: Record<string, unknown>;
  result?: string;
  isError?: boolean;
}

export type PresentationEvent =
  | { kind: "system"; model?: string; sessionId?: string }
  | { kind: "assistant-text"; text: string }
  | { kind: "thinking"; text: string }
  | { kind: "tool-use"; tool: ToolUsePresentation }
  | { kind: "result"; wire: ResultWire }
  | { kind: "error"; message: string };
