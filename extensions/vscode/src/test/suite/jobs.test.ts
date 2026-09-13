import * as assert from 'assert';
import * as vscode from 'vscode';
import { JobRunner, JobStreamEvent } from '../../jobs/jobRunner';

describe('JobRunner Suite', () => {
  describe('subscribeJobEvents', () => {
    const originalFetch = global.fetch;

    afterEach(() => {
      global.fetch = originalFetch;
    });

    it('should parse SSE stream and dispatch onEvent callbacks', async () => {
      const mockServerManager: any = {
        tendrilHome: '/mock/tendril',
        getHealthInfo: async () => ({
          isAlive: true,
          baseUrl: 'http://localhost:5000',
          port: 5000,
          pid: 1234
        })
      };

      const ssePayload = [
        'data: {"kind":"text","text":"Hello world"}\n\n',
        'data: {"kind":"tool_call","tool_name":"dotnet build"}\n\n',
        'event: end\ndata: {"status":"Completed"}\n\n'
      ].join('');

      const stream = new ReadableStream({
        start(controller) {
          controller.enqueue(new TextEncoder().encode(ssePayload));
          controller.close();
        }
      });

      let requestedUrl = '';
      let requestedHeaders: any = {};

      global.fetch = (async (url: string, init?: any) => {
        requestedUrl = url;
        requestedHeaders = init?.headers;
        return new Response(stream, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' }
        });
      }) as any;

      const runner = new JobRunner(mockServerManager);
      const events: JobStreamEvent[] = [];

      const result = await runner.subscribeJobEvents('00418', (evt) => {
        events.push(evt);
      });

      assert.strictEqual(requestedUrl, 'http://localhost:5000/api/jobs/00418/events');
      assert.strictEqual(requestedHeaders['Accept'], 'text/event-stream');
      assert.strictEqual(events.length, 3);
      assert.strictEqual(events[0].kind, 'text');
      assert.strictEqual(events[0].text, 'Hello world');
      assert.strictEqual(events[1].kind, 'tool_call');
      assert.strictEqual(events[1].tool_name, 'dotnet build');
      assert.strictEqual(events[2].kind, 'end');
      assert.strictEqual(events[2].status, 'Completed');
      assert.strictEqual(result.id, '00418');
      assert.strictEqual(result.status, 'Completed');
    });

    it('should handle multi-chunk SSE stream delivery', async () => {
      const mockServerManager: any = {
        tendrilHome: '/mock/tendril',
        getHealthInfo: async () => ({
          isAlive: true,
          baseUrl: 'http://localhost:5000',
          port: 5000,
          pid: 1234
        })
      };

      const chunk1 = 'data: {"kind":"text","te';
      const chunk2 = 'xt":"split payload"}\n\n';
      const chunk3 = 'event: end\ndata: {"status":"Failed"}\n\n';

      const stream = new ReadableStream({
        async start(controller) {
          controller.enqueue(new TextEncoder().encode(chunk1));
          controller.enqueue(new TextEncoder().encode(chunk2));
          controller.enqueue(new TextEncoder().encode(chunk3));
          controller.close();
        }
      });

      global.fetch = (async () => {
        return new Response(stream, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' }
        });
      }) as any;

      const runner = new JobRunner(mockServerManager);
      const events: JobStreamEvent[] = [];

      const result = await runner.subscribeJobEvents('00418', (evt) => {
        events.push(evt);
      });

      assert.strictEqual(events.length, 2);
      assert.strictEqual(events[0].text, 'split payload');
      assert.strictEqual(events[1].status, 'Failed');
      assert.strictEqual(result.status, 'Failed');
    });

    it('should abort stream when cancellation token triggers', async () => {
      const mockServerManager: any = {
        tendrilHome: '/mock/tendril',
        getHealthInfo: async () => ({
          isAlive: true,
          baseUrl: 'http://localhost:5000',
          port: 5000,
          pid: 1234
        })
      };

      const cts = new vscode.CancellationTokenSource();

      let streamController: ReadableStreamDefaultController<Uint8Array> | undefined;
      const stream = new ReadableStream({
        start(controller) {
          streamController = controller;
          controller.enqueue(new TextEncoder().encode('data: {"kind":"text","text":"start"}\n\n'));
        }
      });

      global.fetch = (async (_url: string, init?: any) => {
        if (init?.signal) {
          init.signal.addEventListener('abort', () => {
            try {
              streamController?.error(new DOMException('The operation was aborted.', 'AbortError'));
            } catch {}
          });
        }
        return new Response(stream, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' }
        });
      }) as any;

      const runner = new JobRunner(mockServerManager);
      runner.getJobStatus = async (jobId) => ({ id: jobId, status: 'Running' });

      const promise = runner.subscribeJobEvents('00418', () => {
        cts.cancel();
      }, cts.token);

      const result = await promise;
      assert.strictEqual(result.id, '00418');
      assert.strictEqual(result.status, 'Running');
    });

    it('should append kind query parameters when kinds are provided', async () => {
      const mockServerManager: any = {
        tendrilHome: '/mock/tendril',
        getHealthInfo: async () => ({
          isAlive: true,
          baseUrl: 'http://localhost:5000',
          port: 5000,
          pid: 1234
        })
      };

      let requestedUrl = '';

      global.fetch = (async (url: string) => {
        requestedUrl = url;
        const stream = new ReadableStream({
          start(controller) {
            controller.enqueue(new TextEncoder().encode('event: end\ndata: {"status":"Completed"}\n\n'));
            controller.close();
          }
        });
        return new Response(stream, {
          status: 200,
          headers: { 'Content-Type': 'text/event-stream' }
        });
      }) as any;

      const runner = new JobRunner(mockServerManager);
      await runner.subscribeJobEvents('00438', () => {}, undefined, ['tool_call', 'tool_result']);

      assert.strictEqual(
        requestedUrl,
        'http://localhost:5000/api/jobs/00438/events?kind=tool_call&kind=tool_result'
      );
    });
  });
});
