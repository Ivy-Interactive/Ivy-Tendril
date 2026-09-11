export interface MasterFileData {
  pid: number;
  port: number;
  scheme?: string;
  startedAt?: string;
  heartbeat: string;
}

export interface DiscoveryResult {
  baseUrl: string;
  port: number;
  pid: number;
  scheme: string;
  heartbeat: Date;
  apiKey?: string;
}

export type DiscoveryStatus =
  | { status: 'found'; result: DiscoveryResult }
  | { status: 'stale_heartbeat'; pid: number; lastHeartbeat: Date; message: string }
  | { status: 'dead_process'; pid: number; message: string }
  | { status: 'not_found'; message: string }
  | { status: 'invalid_content'; message: string };

export interface TendrilPlanSummary {
  id: string;
  title: string;
  state: string;
  project?: string;
  level?: string;
}

export interface ServerHealthInfo {
  isAlive: boolean;
  port?: number;
  baseUrl?: string;
  pid?: number;
  activeJobsCount: number;
}
