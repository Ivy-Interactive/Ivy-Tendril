import * as fs from 'fs';
import * as path from 'path';
import * as os from 'os';
import { DiscoveryResult, DiscoveryStatus, MasterFileData, TendrilPlanSummary, TendrilProjectSummary } from './types';

export const HEARTBEAT_TIMEOUT_MS = 90_000;

export function resolveTendrilHome(homeOverride?: string): string {
  if (homeOverride && homeOverride.trim().length > 0) {
    return path.resolve(homeOverride.trim());
  }

  const envHome = process.env.TENDRIL_HOME;
  if (envHome && envHome.trim().length > 0) {
    return path.resolve(envHome.trim());
  }

  return path.join(os.homedir(), '.tendril');
}

export function getMasterFilePath(tendrilHome: string): string {
  return path.join(tendrilHome, '.master');
}

export function parseMasterJson(jsonText: string): MasterFileData | null {
  try {
    const raw = JSON.parse(jsonText);
    if (!raw || typeof raw !== 'object') {
      return null;
    }

    const pid = raw.pid ?? raw.Pid;
    const port = raw.port ?? raw.Port;
    const scheme = raw.scheme ?? raw.Scheme ?? 'http';
    const startedAt = raw.startedAt ?? raw.StartedAt;
    const heartbeat = raw.heartbeat ?? raw.Heartbeat;

    if (typeof pid !== 'number' || typeof port !== 'number' || !heartbeat) {
      return null;
    }

    return {
      pid,
      port,
      scheme,
      startedAt,
      heartbeat: String(heartbeat)
    };
  } catch {
    return null;
  }
}

export function isProcessAlive(pid: number): boolean {
  if (typeof pid !== 'number' || pid <= 0 || !Number.isInteger(pid)) {
    return false;
  }

  try {
    process.kill(pid, 0);
    return true;
  } catch (err: unknown) {
    const error = err as { code?: string };
    return error.code === 'EPERM';
  }
}

export function isHeartbeatFresh(
  heartbeat: string | Date,
  maxAgeMs = HEARTBEAT_TIMEOUT_MS,
  now = Date.now()
): boolean {
  const date = typeof heartbeat === 'string' ? new Date(heartbeat) : heartbeat;
  const time = date.getTime();
  if (isNaN(time)) {
    return false;
  }

  return now - time <= maxAgeMs;
}

export function readApiKeyFromConfig(tendrilHome: string): string | undefined {
  const configPath = path.join(tendrilHome, 'config.yaml');
  if (!fs.existsSync(configPath)) {
    return undefined;
  }

  try {
    const content = fs.readFileSync(configPath, 'utf-8');
    const lines = content.split('\n');
    let inApiSection = false;

    for (const rawLine of lines) {
      const trimmed = rawLine.trimEnd();
      const isTopLevel = trimmed.length > 0 && trimmed[0] !== ' ' && trimmed[0] !== '\t';

      if (isTopLevel) {
        inApiSection = trimmed.toLowerCase().startsWith('api:');
        continue;
      }

      if (!inApiSection) {
        continue;
      }

      const inner = trimmed.trim();
      if (!inner.toLowerCase().startsWith('apikey:')) {
        continue;
      }

      const colonIdx = inner.indexOf(':');
      const val = inner.slice(colonIdx + 1).trim().replace(/^["']|["']$/g, '');
      if (!val || val.startsWith('%')) {
        return undefined;
      }
      return val;
    }
  } catch {
    return undefined;
  }

  return undefined;
}

export function discoverMaster(
  tendrilHome: string,
  checkLiveness = true,
  now = Date.now()
): DiscoveryStatus {
  const masterFile = getMasterFilePath(tendrilHome);
  if (!fs.existsSync(masterFile)) {
    return { status: 'not_found', message: `Master file not found at ${masterFile}` };
  }

  let content: string;
  try {
    content = fs.readFileSync(masterFile, 'utf-8');
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : String(err);
    return { status: 'invalid_content', message: `Could not read .master file: ${message}` };
  }

  const data = parseMasterJson(content);
  if (!data) {
    return { status: 'invalid_content', message: 'Malformed .master file JSON' };
  }

  if (checkLiveness) {
    if (!isProcessAlive(data.pid)) {
      return {
        status: 'dead_process',
        pid: data.pid,
        message: `Tendril server process PID ${data.pid} is not running`
      };
    }

    if (!isHeartbeatFresh(data.heartbeat, HEARTBEAT_TIMEOUT_MS, now)) {
      return {
        status: 'stale_heartbeat',
        pid: data.pid,
        lastHeartbeat: new Date(data.heartbeat),
        message: `Tendril server heartbeat is stale (older than 90s)`
      };
    }
  }

  const scheme = data.scheme || 'http';
  const apiKey = readApiKeyFromConfig(tendrilHome);
  const result: DiscoveryResult = {
    baseUrl: `${scheme}://localhost:${data.port}`,
    port: data.port,
    pid: data.pid,
    scheme,
    heartbeat: new Date(data.heartbeat),
    apiKey
  };

  return { status: 'found', result };
}

export async function pingServer(baseUrl: string, timeoutMs = 3000): Promise<boolean> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const url = `${baseUrl.replace(/\/+$/, '')}/api/ping`;
    const res = await fetch(url, { signal: controller.signal });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

export async function fetchRecentPlans(
  baseUrl: string,
  limit = 5,
  apiKey?: string,
  timeoutMs = 3000
): Promise<TendrilPlanSummary[]> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const url = `${baseUrl.replace(/\/+$/, '')}/api/plans?limit=${limit}`;
    const headers: Record<string, string> = {
      Accept: 'application/json'
    };
    if (apiKey) {
      headers['x-api-key'] = apiKey;
    }

    const res = await fetch(url, { headers, signal: controller.signal });
    if (!res.ok) {
      return [];
    }

    const data = await res.json();
    if (Array.isArray(data)) {
      return data.map((item: Record<string, unknown>) => ({
        id: String(item.id ?? ''),
        title: String(item.title ?? ''),
        state: String(item.state ?? ''),
        project: item.project ? String(item.project) : undefined,
        level: item.level ? String(item.level) : undefined
      }));
    }
    return [];
  } catch {
    return [];
  } finally {
    clearTimeout(timer);
  }
}

export async function fetchProjects(
  baseUrl: string,
  apiKey?: string,
  timeoutMs = 3000
): Promise<TendrilProjectSummary[]> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);

  try {
    const url = `${baseUrl.replace(/\/+$/, '')}/api/projects`;
    const headers: Record<string, string> = {
      Accept: 'application/json'
    };
    if (apiKey) {
      headers['x-api-key'] = apiKey;
    }

    const res = await fetch(url, { headers, signal: controller.signal });
    if (!res.ok) {
      return [];
    }

    const data = await res.json();
    if (Array.isArray(data)) {
      return data.map((item: Record<string, unknown>) => ({
        name: String(item.name ?? ''),
        color: item.color ? String(item.color) : undefined,
        repos: Array.isArray(item.repos) ? item.repos.map((r: unknown) => String(r)) : []
      }));
    }
    return [];
  } catch {
    return [];
  } finally {
    clearTimeout(timer);
  }
}

export function normalizeRepoPath(p: string): string {
  if (!p) return '';
  return path.normalize(p).replace(/[\\/]+$/, '').toLowerCase();
}

export function isWorkspaceManaged(
  workspacePath: string,
  projects: TendrilProjectSummary[]
): { isManaged: boolean; projectName?: string } {
  if (!workspacePath) {
    return { isManaged: true };
  }

  const normalizedWs = normalizeRepoPath(workspacePath);
  for (const project of projects) {
    for (const repo of project.repos) {
      const normalizedRepo = normalizeRepoPath(repo);
      if (
        normalizedWs === normalizedRepo ||
        normalizedWs.startsWith(normalizedRepo + path.sep) ||
        normalizedWs.startsWith(normalizedRepo + '/')
      ) {
        return { isManaged: true, projectName: project.name };
      }
    }
  }

  return { isManaged: false };
}
