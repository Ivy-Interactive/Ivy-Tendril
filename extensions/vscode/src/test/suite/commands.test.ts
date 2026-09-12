import * as assert from 'assert';
import * as vscode from 'vscode';
import { registerPlanCommands } from '../../commands/planCommands';
import { COMMANDS } from '../../constants';
import { IJobRunner, JobListItem, JobResult, JobStatusResult } from '../../jobs/jobRunner';

class MockJobRunner implements IJobRunner {
  public startCreatePlanCalls: Array<{ desc: string; project?: string }> = [];
  public startExecutePlanCalls: Array<{ planId: string; note?: string }> = [];
  public startRetryPlanCalls: Array<{ planId: string; changeRequest: string }> = [];
  public startUpdatePlanCalls: Array<{ planId: string; instructions: string }> = [];
  public getJobStatusCalls: string[] = [];
  public jobsList: JobListItem[] = [];
  public projectsList: string[] = ['ivy-tendril'];

  async startCreatePlan(desc: string, project?: string): Promise<JobResult> {
    this.startCreatePlanCalls.push({ desc, project });
    return { jobId: '00101', status: 'Started', message: 'OK' };
  }

  async startExecutePlan(planId: string, note?: string): Promise<JobResult> {
    this.startExecutePlanCalls.push({ planId, note });
    return { jobId: '00102', status: 'Started', message: 'OK' };
  }

  async startRetryPlan(planId: string, changeRequest: string): Promise<JobResult> {
    this.startRetryPlanCalls.push({ planId, changeRequest });
    return { jobId: '00103', status: 'Started', message: 'OK' };
  }

  async startUpdatePlan(planId: string, instructions: string): Promise<JobResult> {
    this.startUpdatePlanCalls.push({ planId, instructions });
    return { jobId: '00104', status: 'Started', message: 'OK' };
  }

  async getJobStatus(jobId: string): Promise<JobStatusResult> {
    this.getJobStatusCalls.push(jobId);
    return { id: jobId, status: 'Running', message: 'All checks passed' };
  }

  async listJobs(): Promise<JobListItem[]> {
    return this.jobsList;
  }

  async listProjects(): Promise<string[]> {
    return this.projectsList;
  }

  async executeCli(_args: string[]): Promise<{ stdout: string; stderr: string }> {
    return { stdout: '', stderr: '' };
  }
}

describe('Tendril Plan Commands Suite', () => {
  let runner: MockJobRunner;
  let context: any;
  let infoMessages: string[] = [];
  let errorMessages: string[] = [];
  let inputBoxResponses: (string | undefined)[] = [];
  let quickPickResponse: any = undefined;

  const mockServerManager: any = {
    ensureServerRunning: async () => ({ baseUrl: 'http://localhost:5000' }),
    tendrilHome: '/mock/tendril'
  };

  beforeEach(() => {
    runner = new MockJobRunner();
    infoMessages = [];
    errorMessages = [];
    inputBoxResponses = [];
    quickPickResponse = undefined;

    context = {
      subscriptions: []
    };

    vscode.window.showInformationMessage = (async (msg: string) => {
      infoMessages.push(msg);
      return undefined;
    }) as any;

    vscode.window.showErrorMessage = (async (msg: string) => {
      errorMessages.push(msg);
      return undefined;
    }) as any;

    vscode.window.showInputBox = (async () => {
      return inputBoxResponses.shift();
    }) as any;

    vscode.window.showQuickPick = (async () => {
      return quickPickResponse;
    }) as any;

    registerPlanCommands(context, runner, mockServerManager);
  });

  afterEach(() => {
    for (const sub of context.subscriptions) {
      if (sub && typeof sub.dispose === 'function') {
        sub.dispose();
      }
    }
  });

  it('should dispatch tendril.createPlan with user input', async () => {
    inputBoxResponses = ['Implement dark mode'];

    await vscode.commands.executeCommand(COMMANDS.createPlan);

    assert.strictEqual(runner.startCreatePlanCalls.length, 1);
    assert.strictEqual(runner.startCreatePlanCalls[0].desc, 'Implement dark mode');
    assert.strictEqual(runner.startCreatePlanCalls[0].project, 'ivy-tendril');
    assert.ok(infoMessages.some(m => m.includes('Started CreatePlan (Job 00101)')));
  });

  it('should cancel tendril.createPlan when description is empty', async () => {
    inputBoxResponses = [undefined];

    await vscode.commands.executeCommand(COMMANDS.createPlan);

    assert.strictEqual(runner.startCreatePlanCalls.length, 0);
  });

  it('should dispatch tendril.executePlan with argument', async () => {
    await vscode.commands.executeCommand(COMMANDS.executePlan, '00399');

    assert.strictEqual(runner.startExecutePlanCalls.length, 1);
    assert.strictEqual(runner.startExecutePlanCalls[0].planId, '00399');
    assert.ok(infoMessages.some(m => m.includes('Started ExecutePlan (Job 00102)')));
  });

  it('should dispatch tendril.executePlan with prompt when argument is omitted', async () => {
    inputBoxResponses = ['00400'];

    await vscode.commands.executeCommand(COMMANDS.executePlan);

    assert.strictEqual(runner.startExecutePlanCalls.length, 1);
    assert.strictEqual(runner.startExecutePlanCalls[0].planId, '00400');
  });

  it('should cancel tendril.executePlan when planId is not provided', async () => {
    inputBoxResponses = ['   '];

    await vscode.commands.executeCommand(COMMANDS.executePlan);

    assert.strictEqual(runner.startExecutePlanCalls.length, 0);
  });

  it('should dispatch tendril.retryPlan with prompts', async () => {
    inputBoxResponses = ['00399', 'Fix failing unit test'];

    await vscode.commands.executeCommand(COMMANDS.retryPlan);

    assert.strictEqual(runner.startRetryPlanCalls.length, 1);
    assert.strictEqual(runner.startRetryPlanCalls[0].planId, '00399');
    assert.strictEqual(runner.startRetryPlanCalls[0].changeRequest, 'Fix failing unit test');
    assert.ok(infoMessages.some(m => m.includes('Started RetryPlan (Job 00103)')));
  });

  it('should cancel tendril.retryPlan when feedback is cancelled', async () => {
    inputBoxResponses = ['00399', undefined];

    await vscode.commands.executeCommand(COMMANDS.retryPlan);

    assert.strictEqual(runner.startRetryPlanCalls.length, 0);
  });

  it('should dispatch tendril.checkJobStatus with direct jobId', async () => {
    await vscode.commands.executeCommand(COMMANDS.checkJobStatus, '01864');

    assert.strictEqual(runner.getJobStatusCalls.length, 1);
    assert.strictEqual(runner.getJobStatusCalls[0], '01864');
    assert.ok(infoMessages.some(m => m.includes('Job 01864 is Running: All checks passed')));
  });

  it('should prompt via quick pick when jobs are available for checkJobStatus', async () => {
    runner.jobsList = [
      {
        id: '01864',
        type: 'ExecutePlan',
        project: 'ivy-tendril',
        status: 'Running',
        message: 'Building'
      }
    ];
    quickPickResponse = { jobId: '01864' };

    await vscode.commands.executeCommand(COMMANDS.checkJobStatus);

    assert.strictEqual(runner.getJobStatusCalls.length, 1);
    assert.strictEqual(runner.getJobStatusCalls[0], '01864');
    assert.ok(infoMessages.some(m => m.includes('Job 01864 is Running')));
  });
});
