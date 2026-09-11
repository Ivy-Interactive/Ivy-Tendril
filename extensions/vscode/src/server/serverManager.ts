import * as vscode from 'vscode';
import * as cp from 'child_process';
import { CONFIG_KEYS } from '../constants';
import {
  discoverMaster,
  fetchRecentPlans,
  pingServer,
  resolveTendrilHome
} from './masterDiscovery';
import { DiscoveryResult, ServerHealthInfo, TendrilPlanSummary } from './types';

export class ServerManager implements vscode.Disposable {
  private readonly outputChannel: vscode.OutputChannel;
  private spawnedChild: cp.ChildProcess | null = null;
  private isSpawning = false;
  private readonly onDidChangeStateEmitter = new vscode.EventEmitter<ServerHealthInfo>();
  public readonly onDidChangeState = this.onDidChangeStateEmitter.event;

  constructor() {
    this.outputChannel = vscode.window.createOutputChannel('Tendril Server');
  }

  public get tendrilHome(): string {
    const config = vscode.workspace.getConfiguration();
    const homeOverride = config.get<string>(CONFIG_KEYS.homeDirectory);
    return resolveTendrilHome(homeOverride);
  }

  public get isSpawnedByExtension(): boolean {
    return this.spawnedChild !== null && !this.spawnedChild.killed;
  }

  public async getHealthInfo(): Promise<ServerHealthInfo> {
    const discovery = discoverMaster(this.tendrilHome, true);
    if (discovery.status !== 'found') {
      return {
        isAlive: false,
        activeJobsCount: 0
      };
    }

    const isReachable = await pingServer(discovery.result.baseUrl, 2000);
    if (!isReachable) {
      return {
        isAlive: false,
        activeJobsCount: 0,
        baseUrl: discovery.result.baseUrl,
        port: discovery.result.port,
        pid: discovery.result.pid
      };
    }

    return {
      isAlive: true,
      baseUrl: discovery.result.baseUrl,
      port: discovery.result.port,
      pid: discovery.result.pid,
      activeJobsCount: 0
    };
  }

  public async getRecentPlans(limit = 5): Promise<TendrilPlanSummary[]> {
    const health = await this.getHealthInfo();
    if (!health.isAlive || !health.baseUrl) {
      return [];
    }

    const discovery = discoverMaster(this.tendrilHome, false);
    const apiKey = discovery.status === 'found' ? discovery.result.apiKey : undefined;
    return fetchRecentPlans(health.baseUrl, limit, apiKey);
  }

  public async ensureServerRunning(): Promise<DiscoveryResult> {
    const existing = discoverMaster(this.tendrilHome, true);
    if (existing.status === 'found') {
      const ping = await pingServer(existing.result.baseUrl, 2000);
      if (ping) {
        return existing.result;
      }
    }

    const config = vscode.workspace.getConfiguration();
    const autoStart = config.get<boolean>(CONFIG_KEYS.serverAutoStart, true);

    if (!autoStart) {
      throw new Error(
        'Tendril server is not running and tendril.server.autoStart is disabled.'
      );
    }

    return this.startServer();
  }

  public async startServer(): Promise<DiscoveryResult> {
    if (this.isSpawning) {
      throw new Error('Tendril server is already in the process of starting.');
    }

    const existing = discoverMaster(this.tendrilHome, true);
    if (existing.status === 'found') {
      const ping = await pingServer(existing.result.baseUrl, 2000);
      if (ping) {
        this.outputChannel.appendLine(
          `Tendril server is already running on ${existing.result.baseUrl} (PID ${existing.result.pid}).`
        );
        this.notifyStateChanged();
        return existing.result;
      }
    }

    this.isSpawning = true;
    try {
      const config = vscode.workspace.getConfiguration();
      const executable = config.get<string>(CONFIG_KEYS.executablePath, 'tendril');
      const port = config.get<number>(CONFIG_KEYS.serverPort, 0);
      const pollTimeoutMs = config.get<number>(CONFIG_KEYS.serverPollTimeout, 15000);

      const args = ['--web'];
      if (port > 0) {
        args.push(`--port=${port}`);
      }

      this.outputChannel.show(true);
      this.outputChannel.appendLine(`Starting Tendril server: ${executable} ${args.join(' ')}`);
      this.outputChannel.appendLine(`Using TENDRIL_HOME: ${this.tendrilHome}`);

      const child = cp.spawn(executable, args, {
        env: {
          ...process.env,
          TENDRIL_HOME: this.tendrilHome
        },
        stdio: ['ignore', 'pipe', 'pipe']
      });

      this.spawnedChild = child;

      child.stdout?.on('data', (data: Buffer) => {
        this.outputChannel.append(data.toString());
      });

      child.stderr?.on('data', (data: Buffer) => {
        this.outputChannel.append(data.toString());
      });

      child.on('error', (err: Error) => {
        this.outputChannel.appendLine(`Failed to start Tendril server: ${err.message}`);
        this.spawnedChild = null;
        this.notifyStateChanged();
      });

      child.on('exit', (code: number | null, signal: string | null) => {
        this.outputChannel.appendLine(
          `Tendril server exited with code ${code ?? 'null'} (signal: ${signal ?? 'none'}).`
        );
        this.spawnedChild = null;
        this.notifyStateChanged();
      });

      const startTime = Date.now();
      while (Date.now() - startTime < pollTimeoutMs) {
        if (!this.spawnedChild) {
          throw new Error('Tendril server process exited unexpectedly during startup.');
        }

        const current = discoverMaster(this.tendrilHome, true);
        if (current.status === 'found') {
          const isUp = await pingServer(current.result.baseUrl, 1500);
          if (isUp) {
            this.outputChannel.appendLine(
              `Tendril server successfully connected at ${current.result.baseUrl} (PID ${current.result.pid}).`
            );
            this.notifyStateChanged();
            return current.result;
          }
        }

        await new Promise(resolve => setTimeout(resolve, 500));
      }

      throw new Error(
        `Tendril server did not start and become responsive within ${pollTimeoutMs}ms.`
      );
    } finally {
      this.isSpawning = false;
    }
  }

  public async stopServer(): Promise<void> {
    if (this.spawnedChild && !this.spawnedChild.killed) {
      this.outputChannel.appendLine('Stopping spawned Tendril server...');
      const child = this.spawnedChild;
      this.spawnedChild = null;

      child.kill('SIGTERM');

      await new Promise<void>(resolve => {
        const timeout = setTimeout(() => {
          try {
            child.kill('SIGKILL');
          } catch {
            // Ignore error if already dead
          }
          resolve();
        }, 3000);

        child.once('exit', () => {
          clearTimeout(timeout);
          resolve();
        });
      });

      this.outputChannel.appendLine('Tendril server stopped.');
      this.notifyStateChanged();
      return;
    }

    const discovery = discoverMaster(this.tendrilHome, true);
    if (discovery.status === 'found') {
      try {
        process.kill(discovery.result.pid, 'SIGTERM');
        this.outputChannel.appendLine(
          `Sent SIGTERM to external Tendril server process (PID ${discovery.result.pid}).`
        );
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        vscode.window.showWarningMessage(`Could not stop Tendril server: ${message}`);
      }
    } else {
      vscode.window.showInformationMessage('No active Tendril server found.');
    }

    this.notifyStateChanged();
  }

  public async restartServer(): Promise<DiscoveryResult> {
    await this.stopServer();
    await new Promise(resolve => setTimeout(resolve, 1000));
    return this.startServer();
  }

  public notifyStateChanged(): void {
    void this.getHealthInfo().then(info => {
      this.onDidChangeStateEmitter.fire(info);
    });
  }

  public dispose(): void {
    const config = vscode.workspace.getConfiguration();
    const stopOnExit = config.get<boolean>(CONFIG_KEYS.serverStopOnExit, false);

    if (stopOnExit && this.spawnedChild && !this.spawnedChild.killed) {
      try {
        this.spawnedChild.kill('SIGTERM');
      } catch {
        // Suppress errors on disposal
      }
    }

    this.onDidChangeStateEmitter.dispose();
    this.outputChannel.dispose();
  }
}
