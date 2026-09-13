import * as assert from 'assert';
import * as vscode from 'vscode';
import { handleChatRequest } from '../../chat/chatParticipant';
import {
  IJobRunner,
  JobListItem,
  JobResult,
  JobRunner,
  JobStatusResult,
  JobStreamEvent
} from '../../jobs/jobRunner';

class MockChatResponseStream {
  public markdownOutput: string[] = [];
  public progressOutput: string[] = [];

  markdown(val: string): void {
    this.markdownOutput.push(val);
  }

  progress(val: string): void {
    this.progressOutput.push(val);
  }

  anchor(): void {}
  button(): void {}
  filetree(): void {}
  reference(): void {}
  push(): void {}
}

class TestJobRunner implements IJobRunner {
  public startCreatePlanCalls: Array<{ desc: string; project?: string }> = [];
  public startExecutePlanCalls: Array<{ planId: string; note?: string }> = [];
  public startRetryPlanCalls: Array<{ planId: string; changeRequest: string }> = [];
  public startUpdatePlanCalls: Array<{ planId: string; instructions: string }> = [];
  public getJobStatusCalls: string[] = [];
  public subscribeJobEventsCalls: Array<{
    jobId: string;
    onEvent: (event: JobStreamEvent) => void;
    token?: any;
  }> = [];
  public listJobsCalls = 0;
  public listProjectsCalls = 0;
  public executeCliCalls: string[][] = [];

  async subscribeJobEvents(
    jobId: string,
    onEvent: (event: JobStreamEvent) => void,
    cancellationToken?: any
  ): Promise<JobStatusResult> {
    this.subscribeJobEventsCalls.push({ jobId, onEvent, token: cancellationToken });
    return { id: jobId, status: 'Completed' };
  }

  async startCreatePlan(desc: string, project?: string): Promise<JobResult> {
    this.startCreatePlanCalls.push({ desc, project });
    return { jobId: '00001', status: 'Started', message: 'Created' };
  }

  async startExecutePlan(planId: string, note?: string): Promise<JobResult> {
    this.startExecutePlanCalls.push({ planId, note });
    return { jobId: '00002', status: 'Started', message: 'Executing' };
  }

  async startRetryPlan(planId: string, changeRequest: string): Promise<JobResult> {
    this.startRetryPlanCalls.push({ planId, changeRequest });
    return { jobId: '00003', status: 'Started', message: 'Retrying' };
  }

  async startUpdatePlan(planId: string, instructions: string): Promise<JobResult> {
    this.startUpdatePlanCalls.push({ planId, instructions });
    return { jobId: '00004', status: 'Started', message: 'Updating' };
  }

  async getJobStatus(jobId: string): Promise<JobStatusResult> {
    this.getJobStatusCalls.push(jobId);
    return { id: jobId, status: 'Running', message: 'Implementing changes' };
  }

  async listJobs(): Promise<JobListItem[]> {
    this.listJobsCalls++;
    return [
      {
        id: '01864',
        type: 'ExecutePlan',
        project: 'ivy-tendril',
        status: 'Running',
        message: 'Running build',
        planId: '00399'
      }
    ];
  }

  async listProjects(): Promise<string[]> {
    this.listProjectsCalls++;
    return ['ivy-tendril', 'growth-hack'];
  }

  async executeCli(args: string[]): Promise<{ stdout: string; stderr: string }> {
    this.executeCliCalls.push(args);
    return { stdout: '', stderr: '' };
  }
}

const mockServerManager: any = {
  ensureServerRunning: async () => ({
    baseUrl: 'http://localhost:5000',
    port: 5000,
    pid: 1234,
    scheme: 'http',
    heartbeat: new Date()
  }),
  getHealthInfo: async () => ({
    isAlive: true,
    baseUrl: 'http://localhost:5000',
    port: 5000,
    pid: 1234
  }),
  tendrilHome: '/mock/tendril'
};

describe('Tendril Chat Participant Suite', () => {
  describe('Slash command handling in handleChatRequest', () => {
    it('should handle /plan with valid prompt', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'plan',
        prompt: 'Add dark mode support'
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.startCreatePlanCalls.length, 1);
      assert.strictEqual(runner.startCreatePlanCalls[0].desc, 'Add dark mode support');
      assert.strictEqual(runner.startCreatePlanCalls[0].project, 'ivy-tendril');
      assert.ok(stream.markdownOutput.length > 0);
      assert.ok(stream.markdownOutput[0].includes('Started **CreatePlan** job'));
      assert.ok(stream.markdownOutput[0].includes('00001'));
    });

    it('should prompt for description when /plan prompt is empty', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'plan',
        prompt: '   '
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.startCreatePlanCalls.length, 0);
      assert.ok(stream.markdownOutput[0].includes('Please provide a description'));
    });

    it('should handle /run with valid planId', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'run',
        prompt: '00399'
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.startExecutePlanCalls.length, 1);
      assert.strictEqual(runner.startExecutePlanCalls[0].planId, '00399');
      assert.ok(stream.markdownOutput.length > 0);
      assert.ok(stream.markdownOutput[0].includes('Started **ExecutePlan** job for Plan `00399`'));
      assert.ok(stream.markdownOutput[0].includes('00002'));
    });

    it('should prompt for planId when /run prompt is empty', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'run',
        prompt: ''
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.startExecutePlanCalls.length, 0);
      assert.ok(stream.markdownOutput[0].includes('Please specify a plan ID'));
    });

    it('should handle /status with specific jobId', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'status',
        prompt: '01864'
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.getJobStatusCalls.length, 1);
      assert.strictEqual(runner.getJobStatusCalls[0], '01864');
      assert.ok(stream.markdownOutput[0].includes('### Job `01864`'));
      assert.ok(stream.markdownOutput[0].includes('Running'));
      assert.ok(stream.markdownOutput[0].includes('Implementing changes'));
    });

    it('should handle /status without prompt and output active jobs table', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'status',
        prompt: ''
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.listJobsCalls, 1);
      assert.ok(stream.markdownOutput[0].includes('### Active Jobs'));
      assert.ok(stream.markdownOutput[0].includes('01864'));
      assert.ok(stream.markdownOutput[0].includes('ExecutePlan'));
    });

    it('should handle /retry with planId and feedback', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'retry',
        prompt: '00399 Fix failing tests'
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.startRetryPlanCalls.length, 1);
      assert.strictEqual(runner.startRetryPlanCalls[0].planId, '00399');
      assert.strictEqual(runner.startRetryPlanCalls[0].changeRequest, 'Fix failing tests');
      assert.ok(stream.markdownOutput[0].includes('Started **RetryPlan** job for Plan `00399`'));
      assert.ok(stream.markdownOutput[0].includes('Fix failing tests'));
      assert.ok(stream.markdownOutput[0].includes('00003'));
    });

    it('should prompt when /retry is missing feedback', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: 'retry',
        prompt: '00399'
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.startRetryPlanCalls.length, 0);
      assert.ok(stream.markdownOutput[0].includes('Please specify both a plan ID and feedback'));
    });

    it('should output default help markdown for empty or unknown command', async () => {
      const runner = new TestJobRunner();
      const stream = new MockChatResponseStream();
      const request: any = {
        command: undefined,
        prompt: 'hello'
      };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.ok(stream.markdownOutput[0].includes('Hello! I am **Tendril**'));
      assert.ok(stream.markdownOutput[0].includes('@tendril /plan'));
      assert.ok(stream.markdownOutput[0].includes('@tendril /run'));
      assert.ok(stream.markdownOutput[0].includes('@tendril /status'));
      assert.ok(stream.markdownOutput[0].includes('@tendril /retry'));
    });
  });

  describe('CLI command construction matching Tendril conventions', () => {
    it('should construct exact equals-form arguments for CreatePlan', async () => {
      const runner = new JobRunner(mockServerManager);
      let capturedArgs: string[] = [];
      runner.executeCli = async (args: string[]) => {
        capturedArgs = args;
        return { stdout: 'Job started: 01999\n', stderr: '' };
      };

      const res = await runner.startCreatePlan('Build authentication', 'ivy-tendril');
      assert.deepStrictEqual(capturedArgs, [
        'job',
        'start',
        'CreatePlan',
        '--description=Build authentication',
        '--project=ivy-tendril'
      ]);
      assert.strictEqual(res.jobId, '01999');
      assert.strictEqual(res.status, 'Started');
    });

    it('should construct exact arguments for ExecutePlan with optional note', async () => {
      const runner = new JobRunner(mockServerManager);
      let capturedArgs: string[] = [];
      runner.executeCli = async (args: string[]) => {
        capturedArgs = args;
        return { stdout: 'Job started: 01998\n', stderr: '' };
      };

      const res = await runner.startExecutePlan('00399', 'Priority fix');
      assert.deepStrictEqual(capturedArgs, [
        'job',
        'start',
        'ExecutePlan',
        '00399',
        '--note=Priority fix'
      ]);
      assert.strictEqual(res.jobId, '01998');
    });

    it('should construct exact equals-form arguments for RetryPlan', async () => {
      const runner = new JobRunner(mockServerManager);
      let capturedArgs: string[] = [];
      runner.executeCli = async (args: string[]) => {
        capturedArgs = args;
        return { stdout: 'Job started: 01997\n', stderr: '' };
      };

      const res = await runner.startRetryPlan('00399', 'Fix format linter');
      assert.deepStrictEqual(capturedArgs, [
        'job',
        'start',
        'RetryPlan',
        '00399',
        '--change-request=Fix format linter'
      ]);
      assert.strictEqual(res.jobId, '01997');
    });

    it('should construct exact equals-form arguments for UpdatePlan', async () => {
      const runner = new JobRunner(mockServerManager);
      let capturedArgs: string[] = [];
      runner.executeCli = async (args: string[]) => {
        capturedArgs = args;
        return { stdout: 'Job started: 01996\n', stderr: '' };
      };

      const res = await runner.startUpdatePlan('00399', 'Update database schema');
      assert.deepStrictEqual(capturedArgs, [
        'job',
        'start',
        'UpdatePlan',
        '00399',
        '--instructions=Update database schema'
      ]);
      assert.strictEqual(res.jobId, '01996');
    });
  });

  describe('Live streaming in handleChatRequest', () => {
    it('should subscribe to jobRunner.subscribeJobEvents on /plan and render streamed output', async () => {
      const runner = new TestJobRunner();
      runner.subscribeJobEvents = async (jobId, onEvent) => {
        runner.subscribeJobEventsCalls.push({ jobId, onEvent });
        onEvent({ kind: 'text', text: 'Generating plan structure...' });
        onEvent({ kind: 'tool_call', tool_name: 'git status' });
        return { id: jobId, status: 'Completed', planId: '00418' };
      };

      const stream = new MockChatResponseStream();
      const request: any = { command: 'plan', prompt: 'Add new feature' };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.subscribeJobEventsCalls.length, 1);
      assert.strictEqual(runner.subscribeJobEventsCalls[0].jobId, '00001');
      assert.ok(stream.markdownOutput.some(m => m.includes('Generating plan structure...')));
      assert.ok(stream.markdownOutput.some(m => m.includes('🔧 `git status`')));
      assert.ok(stream.markdownOutput.some(m => m.includes('Completed ✅')));
      assert.ok(stream.markdownOutput.some(m => m.includes('@tendril /run 00418')));
    });

    it('should subscribe to jobRunner.subscribeJobEvents on /run and render streamed output', async () => {
      const runner = new TestJobRunner();
      runner.subscribeJobEvents = async (jobId, onEvent) => {
        runner.subscribeJobEventsCalls.push({ jobId, onEvent });
        onEvent({ kind: 'text', text: 'Executing plan steps...' });
        return { id: jobId, status: 'Completed' };
      };

      const stream = new MockChatResponseStream();
      const request: any = { command: 'run', prompt: '00418' };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.subscribeJobEventsCalls.length, 1);
      assert.strictEqual(runner.subscribeJobEventsCalls[0].jobId, '00002');
      assert.ok(stream.markdownOutput.some(m => m.includes('Executing plan steps...')));
      assert.ok(stream.markdownOutput.some(m => m.includes('Completed ✅')));
    });

    it('should subscribe to jobRunner.subscribeJobEvents on /retry and render streamed output', async () => {
      const runner = new TestJobRunner();
      runner.subscribeJobEvents = async (jobId, onEvent) => {
        runner.subscribeJobEventsCalls.push({ jobId, onEvent });
        onEvent({ kind: 'text', text: 'Retrying plan with feedback...' });
        return { id: jobId, status: 'Completed' };
      };

      const stream = new MockChatResponseStream();
      const request: any = { command: 'retry', prompt: '00418 Fix compile issues' };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.subscribeJobEventsCalls.length, 1);
      assert.strictEqual(runner.subscribeJobEventsCalls[0].jobId, '00003');
      assert.ok(stream.markdownOutput.some(m => m.includes('Retrying plan with feedback...')));
      assert.ok(stream.markdownOutput.some(m => m.includes('Completed ✅')));
    });

    it('should stream live progress on /status <jobId> when job is running', async () => {
      const runner = new TestJobRunner();
      runner.getJobStatus = async (jobId) => ({ id: jobId, status: 'Running', message: 'In progress' });
      runner.subscribeJobEvents = async (jobId, onEvent) => {
        runner.subscribeJobEventsCalls.push({ jobId, onEvent });
        onEvent({ kind: 'text', text: 'Currently compiling...' });
        return { id: jobId, status: 'Completed' };
      };

      const stream = new MockChatResponseStream();
      const request: any = { command: 'status', prompt: '01864' };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.subscribeJobEventsCalls.length, 1);
      assert.strictEqual(runner.subscribeJobEventsCalls[0].jobId, '01864');
      assert.ok(stream.markdownOutput.some(m => m.includes('Currently compiling...')));
    });

    it('should not stream on /status <jobId> --no-follow', async () => {
      const runner = new TestJobRunner();
      runner.getJobStatus = async (jobId) => ({ id: jobId, status: 'Running', message: 'In progress' });

      const stream = new MockChatResponseStream();
      const request: any = { command: 'status', prompt: '01864 --no-follow' };

      await handleChatRequest(request, stream as any, runner, mockServerManager);

      assert.strictEqual(runner.subscribeJobEventsCalls.length, 0);
    });

    it('should handle cancellation gracefully when CancellationToken is cancelled during streaming', async () => {
      const runner = new TestJobRunner();
      const cts = new vscode.CancellationTokenSource();

      runner.subscribeJobEvents = async (jobId, _onEvent, _token) => {
        cts.cancel();
        return { id: jobId, status: 'Running' };
      };

      const stream = new MockChatResponseStream();
      const request: any = { command: 'run', prompt: '00418' };

      await handleChatRequest(request, stream as any, runner, mockServerManager, cts.token);

      assert.ok(
        stream.markdownOutput.some(m =>
          m.includes('Stopped following job events')
        )
      );
    });
  });
});
