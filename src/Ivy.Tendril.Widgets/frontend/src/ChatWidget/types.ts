import type { IvyEventHandler } from "../TendrilProcessViewer/types";

export type { IvyEventHandler };

export interface ChatMessageDto {
  id: string;
  role: "user" | "assistant" | "system";
  content: string;
  timestamp: string;
  agentId?: string;
  modelId?: string;
  rawStream?: string;
  effort?: string;
}

export interface ChatJobDto {
  id: string;
  type: string;
  status: string;
  planId?: string;
  planTitle?: string;
  statusMessage?: string;
}

export interface ChatSessionDto {
  id: string;
  title: string;
  agentId: string;
  modelId: string;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessageDto[];
  status?: "generating" | "waiting" | "done";
  effort?: string;
  spawnedJobs?: ChatJobDto[];
}

export interface ModelOptionDto {
  id: string;
  displayName: string;
}

export interface EffortOptionDto {
  id: string;
  displayName: string;
}

export interface AgentOptionDto {
  id: string;
  label: string;
  /** The C# Icons enum name of the agent's brand mark, resolved by `BrandIcon`. */
  icon?: string;
  /** The agent's model catalog, so the picker can offer models before the agent is selected. */
  models?: ModelOptionDto[];
  supportsEffort?: boolean;
}

export interface ChatAttachmentDto {
  name: string;
  contentType: string;
  size: number;
  base64Data?: string;
  localPath?: string;
  lineCount?: number;
  previewUrl?: string;
  fileId?: string;
  uploadProgress?: number;
  uploadStatus?: "pending" | "uploading" | "finished" | "failed";
  error?: string;
}

export interface ChatQueuedMessageDto {
  id: string;
  prompt: string;
  attachments?: ChatAttachmentDto[];
}

export interface ChatWidgetProps {
  id: string;
  activeSessionId?: string | null;
  streamingSessionId?: string | null;
  uploadUrl?: string;
  /** WebSocket endpoint used to transcribe voice input when the Web Speech API cannot work here. */
  transcriptionUrl?: string;
  sessions?: ChatSessionDto[];
  agents?: AgentOptionDto[];
  models?: ModelOptionDto[];
  efforts?: EffortOptionDto[];
  selectedAgent?: string;
  selectedModel?: string;
  selectedEffort?: string;
  supportsEffort?: boolean;
  isStreaming?: boolean;
  streamingText?: string;
  queuedMessages?: ChatQueuedMessageDto[];
  runningJobs?: ChatJobDto[];
  /** Shown above the headline while the conversation is empty, e.g. "Good Morning, Joel!". */
  greeting?: string;
  headline?: string;
  /** Hosted inside another page (the plan chat panel): no title bar, a tighter composer. */
  embedded?: boolean;
  events?: string[];
  eventHandler?: IvyEventHandler;
}
