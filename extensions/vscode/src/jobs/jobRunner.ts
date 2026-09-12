import * as vscode from 'vscode';
import * as cp from 'child_process';
import { CONFIG_KEYS } from '../constants';
import { discoverMaster, resolveTendrilHome } from '../server/masterDiscovery';
import { ServerManager } from '../server/serverManager';

export interface JobStreamEvent {
  kind?: string;
  text?: string;
  delta?: boolean;
  content?: string;
  tool_name?: string;
  tool_use_id?: string;
  input?: Record<string, unknown>;
  output?: string;
  is_error?: boolean;
  message?: string;
  status?: string;
  [key: string]: unknown;
}

export interface JobResult {
  jobId?: string;
  status?: string;
  message: string;
}

export interface JobStatusResult {
  id: string;
  status: string;
  message?: string;
  planId?: string;
}

export interface JobListItem {
  id: string;
  type: string;
  project: string;
  status: string;
  message?: string;
  planId?: string;
  started?: string;
  duration?: string;
}

export interface IJobRunner {
  startCreatePlan(description: string, project?: string): Promise<JobResult>;
  startExecutePlan(planId: string, note?: string): Promise<JobResult>;
  startRetryPlan(planId: string, changeRequest: string): Promise<JobResult>;
  startUpdatePlan(planId: string, instructions: string): Promise<JobResult>;
  getJobStatus(jobId: string): Promise<JobStatusResult>;
  subscribeJobEvents(
    jobId: string,
    onEvent: (event: JobStreamEvent) => void,
    cancellationToken?: vscode.CancellationToken
  ): Promise<JobStatusResult>;
  listJobs(): Promise<JobListItem[]>;
  listProjects(): Promise<string[]>;
  executeCli(args: string[]): Promise<{ stdout: string; stderr: string }>;
}

export class JobRunner implements IJobRunner {
  constructor(private readonly serverManager?: ServerManager) {}

  public async executeCli(args: string[]): Promise<{ stdout: string; stderr: string }> {
    const config = vscode.workspace.getConfiguration();
    const executable = config.get<string>(CONFIG_KEYS.executablePath, 'tendril');
    const tendrilHome = this.serverManager
      ? this.serverManager.tendrilHome
      : resolveTendrilHome(config.get<string>(CONFIG_KEYS.homeDirectory));

    return new Promise((resolve, reject) => {
      cp.execFile(
        executable,
        args,
        {
          env: {
            ...process.env,
            TENDRIL_HOME: tendrilHome
          }
        },
        (err, stdout, stderr) => {
          if (err) {
            const errorOutput = stderr ? stderr.toString().trim() : '';
            const stdOutput = stdout ? stdout.toString().trim() : '';
            reject(new Error(errorOutput || stdOutput || err.message));
          } else {
            resolve({
              stdout: stdout ? stdout.toString() : '',
              stderr: stderr ? stderr.toString() : ''
            });
          }
        }
      );
    });
  }

  private extractJobId(output: string): string | undefined {
    const match = output.match(/Job started:\s*([^\s\r\n]+)/i);
    return match ? match[1] : undefined;
  }

  public async startCreatePlan(description: string, project?: string): Promise<JobResult> {
    const proj = project || 'default';
    const args = [
      'job',
      'start',
      'CreatePlan',
      `--description=${description}`,
      `--project=${proj}`
    ];
    const { stdout } = await this.executeCli(args);
    const jobId = this.extractJobId(stdout);
    return {
      jobId,
      status: 'Started',
      message: stdout.trim()
    };
  }

  public async startExecutePlan(planId: string, note?: string): Promise<JobResult> {
    const args = ['job', 'start', 'ExecutePlan', planId];
    if (note) {
      args.push(`--note=${note}`);
    }
    const { stdout } = await this.executeCli(args);
    const jobId = this.extractJobId(stdout);
    return {
      jobId,
      status: 'Started',
      message: stdout.trim()
    };
  }

  public async startRetryPlan(planId: string, changeRequest: string): Promise<JobResult> {
    const args = [
      'job',
      'start',
      'RetryPlan',
      planId,
      `--change-request=${changeRequest}`
    ];
    const { stdout } = await this.executeCli(args);
    const jobId = this.extractJobId(stdout);
    return {
      jobId,
      status: 'Started',
      message: stdout.trim()
    };
  }

  public async startUpdatePlan(planId: string, instructions: string): Promise<JobResult> {
    const args = [
      'job',
      'start',
      'UpdatePlan',
      planId,
      `--instructions=${instructions}`
    ];
    const { stdout } = await this.executeCli(args);
    const jobId = this.extractJobId(stdout);
    return {
      jobId,
      status: 'Started',
      message: stdout.trim()
    };
  }

  public async listJobs(): Promise<JobListItem[]> {
    try {
      const { stdout } = await this.executeCli(['job', 'list', '--json']);
      const lines = stdout.split(/\r?\n/).filter(l => l.trim().length > 0);
      if (lines.length <= 1) {
        return [];
      }
      const result: JobListItem[] = [];
      for (let i = 1; i < lines.length; i++) {
        const cols = lines[i].split('\t');
        if (cols.length >= 4) {
          result.push({
            id: cols[0].trim(),
            type: cols[1].trim(),
            project: cols[2].trim(),
            status: cols[3].trim(),
            message: cols[4] && cols[4].trim().length > 0 ? cols[4].trim() : undefined,
            planId: cols[5] && cols[5].trim().length > 0 ? cols[5].trim() : undefined,
            started: cols[6] && cols[6].trim().length > 0 ? cols[6].trim() : undefined,
            duration: cols[7] && cols[7].trim().length > 0 ? cols[7].trim() : undefined
          });
        }
      }
      return result;
    } catch {
      return [];
    }
  }

  public async getJobStatus(jobId: string): Promise<JobStatusResult> {
    if (this.serverManager) {
      try {
        const health = await this.serverManager.getHealthInfo();
        if (health.isAlive && health.baseUrl) {
          const url = `${health.baseUrl.replace(/\/+$/, '')}/api/jobs/${encodeURIComponent(jobId)}`;
          const res = await fetch(url);
          if (res.ok) {
            const data = (await res.json()) as { id: string; status: string; message?: string };
            return {
              id: data.id,
              status: data.status,
              message: data.message
            };
          }
        }
      } catch {
        // Fall back to listing jobs
      }
    }

    const jobs = await this.listJobs();
    const found = jobs.find(j => j.id === jobId || j.id.endsWith(jobId));
    if (found) {
      return {
        id: found.id,
        status: found.status,
        message: found.message
      };
    }

    return {
      id: jobId,
      status: 'Unknown',
      message: 'Job status not found'
    };
  }

  public async subscribeJobEvents(
    jobId: string,
    onEvent: (event: JobStreamEvent) => void,
    cancellationToken?: vscode.CancellationToken
  ): Promise<JobStatusResult> {
    let baseUrl: string | undefined;
    let apiKey: string | undefined;

    if (this.serverManager) {
      try {
        const health = await this.serverManager.getHealthInfo();
        if (health.isAlive && health.baseUrl) {
          baseUrl = health.baseUrl;
          const discovery = discoverMaster(this.serverManager.tendrilHome, false);
          if (discovery.status === 'found') {
            apiKey = discovery.result.apiKey;
          }
        }
      } catch {
        // Fall back
      }
    }

    if (!baseUrl) {
      return await this.getJobStatus(jobId);
    }

    const abortController = new AbortController();
    let cancellationListener: vscode.Disposable | undefined;
    if (cancellationToken) {
      if (cancellationToken.isCancellationRequested) {
        abortController.abort();
      } else {
        cancellationListener = cancellationToken.onCancellationRequested(() => {
          abortController.abort();
        });
      }
    }

    const url = `${baseUrl.replace(/\/+$/, '')}/api/jobs/${encodeURIComponent(jobId)}/events`;
    const headers: Record<string, string> = {
      Accept: 'text/event-stream'
    };
    if (apiKey) {
      headers['x-api-key'] = apiKey;
    }

    let finalStatus: string | undefined;

    const parseSseMessage = (message: string) => {
      let eventType = '';
      const dataLines: string[] = [];
      for (const line of message.split(/\r?\n/)) {
        if (line.startsWith(':')) {
          continue;
        }
        if (line.startsWith('event:')) {
          eventType = line.slice(6).trim();
        } else if (line.startsWith('data:')) {
          dataLines.push(line.slice(5).trimStart());
        }
      }
      const dataStr = dataLines.join('\n');
      if (!dataStr) {
        return;
      }

      try {
        const parsed = JSON.parse(dataStr);
        if (eventType === 'end') {
          if (parsed.status) {
            finalStatus = parsed.status;
          }
          onEvent({ kind: 'end', status: parsed.status, ...parsed });
        } else {
          if (parsed.status && !finalStatus) {
            finalStatus = parsed.status;
          }
          onEvent(parsed);
        }
      } catch {
        onEvent({ text: dataStr, content: dataStr });
      }
    };

    try {
      const res = await fetch(url, {
        headers,
        signal: abortController.signal
      });

      if (!res.ok || !res.body) {
        return await this.getJobStatus(jobId);
      }

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) {
          break;
        }
        buffer += decoder.decode(value, { stream: true });
        let match;
        while ((match = buffer.match(/\r?\n\r?\n/)) !== null && match.index !== undefined) {
          const idx = match.index;
          const message = buffer.slice(0, idx);
          buffer = buffer.slice(idx + match[0].length);
          parseSseMessage(message);
        }
      }

      if (buffer.trim().length > 0) {
        parseSseMessage(buffer);
      }
    } catch (err: unknown) {
      if (cancellationToken?.isCancellationRequested || (err instanceof Error && err.name === 'AbortError')) {
        return await this.getJobStatus(jobId);
      }
      return await this.getJobStatus(jobId);
    } finally {
      cancellationListener?.dispose();
    }

    if (finalStatus) {
      return {
        id: jobId,
        status: finalStatus
      };
    }

    return await this.getJobStatus(jobId);
  }

  public async listProjects(): Promise<string[]> {
    try {
      const { stdout } = await this.executeCli(['project', 'list']);
      return stdout
        .split(/\r?\n/)
        .map(l => l.trim())
        .filter(l => l.length > 0 && !l.startsWith('Error'));
    } catch {
      return [];
    }
  }
}
