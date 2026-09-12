import * as vscode from 'vscode';
import { IJobRunner, JobStreamEvent } from '../jobs/jobRunner';
import { ServerManager } from '../server/serverManager';

export const CHAT_PARTICIPANT_ID = 'tendril.chatParticipant';

export async function streamJobProgress(
  jobId: string,
  response: vscode.ChatResponseStream,
  jobRunner: IJobRunner,
  token?: vscode.CancellationToken
): Promise<void> {
  const startTime = Date.now();
  let discoveredPlanId: string | undefined;

  const onEvent = (evt: JobStreamEvent) => {
    if (token?.isCancellationRequested) {
      return;
    }

    if (evt.planId || evt.plan_id) {
      discoveredPlanId = String(evt.planId || evt.plan_id);
    }

    if (evt.tool_name) {
      response.progress(`Running tool: ${evt.tool_name}...`);
    } else if (evt.status_message || evt.message) {
      response.progress(String(evt.status_message || evt.message));
    } else if (evt.kind === 'thinking') {
      response.progress('Thinking...');
    }

    if (evt.kind === 'text' && evt.text) {
      response.markdown(evt.text);
    } else if (evt.content && evt.kind !== 'thinking') {
      response.markdown(evt.content);
    } else if (evt.kind === 'tool_call' && evt.tool_name) {
      response.markdown(`\n> 🔧 \`${evt.tool_name}\`\n\n`);
    } else if (evt.kind === 'error' || (evt.kind === 'tool_result' && evt.is_error)) {
      const errMsg = evt.message || evt.output || 'Unknown error';
      response.markdown(`\n> ⚠️ **Error:** ${errMsg}\n\n`);
    }
  };

  const finalStatus = await jobRunner.subscribeJobEvents(jobId, onEvent, token);

  if (token?.isCancellationRequested) {
    response.markdown(
      '\n\n*(Stopped following job events. The job continues running in the background.)*'
    );
    return;
  }

  const elapsedSeconds = Math.max(1, Math.round((Date.now() - startTime) / 1000));
  const planId = finalStatus.planId || discoveredPlanId;

  let nextStep = '';
  if (finalStatus.status === 'Completed' && planId) {
    nextStep = `\n- **Next Command:** \`@tendril /run ${planId}\``;
  } else if (finalStatus.status === 'Failed' && planId) {
    nextStep = `\n- **Next Command:** \`@tendril /retry ${planId} <feedback>\``;
  }

  const statusBadge =
    finalStatus.status === 'Completed'
      ? 'Completed ✅'
      : finalStatus.status === 'Failed'
      ? 'Failed ❌'
      : finalStatus.status;

  const planInfo = planId ? `\n- **Plan:** \`${planId}\`` : '';

  response.markdown(
    `\n\n---\n### Job \`${jobId}\` ${statusBadge}\n` +
      `- **Status:** ${finalStatus.status}\n` +
      `- **Duration:** ${elapsedSeconds}s` +
      planInfo +
      nextStep +
      '\n'
  );
}

export async function handleChatRequest(
  request: vscode.ChatRequest,
  response: vscode.ChatResponseStream,
  jobRunner: IJobRunner,
  serverManager: ServerManager,
  _token?: vscode.CancellationToken
): Promise<void> {
  const prompt = (request.prompt || '').trim();

  switch (request.command) {
    case 'plan': {
      if (!prompt) {
        response.markdown(
          'Please provide a description for the plan, for example: `@tendril /plan Add dark mode support`.'
        );
        return;
      }

      response.progress('Ensuring Tendril server is running...');
      await serverManager.ensureServerRunning();

      response.progress('Determining project...');
      const projects = await jobRunner.listProjects();
      let project = projects.length > 0 ? projects[0] : 'default';

      const wsFolders = vscode.workspace.workspaceFolders;
      if (wsFolders && wsFolders.length > 0) {
        const wsName = wsFolders[0].name.toLowerCase();
        const matched = projects.find(p => p.toLowerCase() === wsName);
        if (matched) {
          project = matched;
        }
      }

      response.progress(`Starting CreatePlan job for project "${project}"...`);
      const res = await jobRunner.startCreatePlan(prompt, project);

      const jobHeader = res.jobId
        ? `Started **CreatePlan** job: \`${res.jobId}\``
        : 'Started **CreatePlan** job.';

      response.markdown(
        `${jobHeader}\n\n` +
          `- **Project:** ${project}\n` +
          `- **Description:** ${prompt}\n\n` +
          (res.jobId ? `Use \`@tendril /status ${res.jobId}\` to monitor progress.` : '')
      );

      if (res.jobId) {
        await streamJobProgress(res.jobId, response, jobRunner, _token);
      }
      break;
    }

    case 'run': {
      if (!prompt) {
        response.markdown(
          'Please specify a plan ID to run, for example: `@tendril /run 00399`.'
        );
        return;
      }

      const planId = prompt.split(/\s+/)[0];
      response.progress(`Starting ExecutePlan job for plan ${planId}...`);
      await serverManager.ensureServerRunning();
      const res = await jobRunner.startExecutePlan(planId);

      const jobHeader = res.jobId
        ? `Started **ExecutePlan** job for Plan \`${planId}\` (Job ID: \`${res.jobId}\`).`
        : `Started **ExecutePlan** job for Plan \`${planId}\`.`;

      response.markdown(
        `${jobHeader}\n\n` +
          (res.jobId ? `Use \`@tendril /status ${res.jobId}\` to monitor execution.` : '')
      );

      if (res.jobId) {
        await streamJobProgress(res.jobId, response, jobRunner, _token);
      }
      break;
    }

    case 'status': {
      await serverManager.ensureServerRunning();

      if (prompt) {
        const parts = prompt.split(/\s+/);
        const jobId = parts[0];
        const noFollow = parts.includes('--no-follow');

        response.progress(`Checking status for job ${jobId}...`);
        const status = await jobRunner.getJobStatus(jobId);
        response.markdown(
          `### Job \`${status.id}\`\n\n` +
            `- **Status:** ${status.status}\n` +
            `- **Message:** ${status.message || 'No status message'}`
        );

        if (
          !noFollow &&
          (status.status === 'Running' || status.status === 'Pending' || status.status === 'Queued')
        ) {
          await streamJobProgress(jobId, response, jobRunner, _token);
        }
      } else {
        response.progress('Fetching active jobs...');
        const jobs = await jobRunner.listJobs();
        if (jobs.length === 0) {
          response.markdown('No active jobs found.');
          return;
        }

        let md =
          '### Active Jobs\n\n' +
          '| Job ID | Type | Plan | Status | Message |\n' +
          '| --- | --- | --- | --- | --- |\n';

        for (const j of jobs) {
          md += `| ${j.id} | ${j.type} | ${j.planId || '-'} | ${j.status} | ${j.message || '-'} |\n`;
        }

        response.markdown(md);
      }
      break;
    }

    case 'retry': {
      const match = prompt.match(/^([^\s]+)\s+(.+)$/s);
      if (!match) {
        response.markdown(
          'Please specify both a plan ID and feedback, for example: `@tendril /retry 00399 Fix failing tests`.'
        );
        return;
      }

      const planId = match[1];
      const feedback = match[2];

      response.progress(`Starting RetryPlan job for plan ${planId}...`);
      await serverManager.ensureServerRunning();
      const res = await jobRunner.startRetryPlan(planId, feedback);

      const jobHeader = res.jobId
        ? `Started **RetryPlan** job for Plan \`${planId}\` (Job ID: \`${res.jobId}\`).`
        : `Started **RetryPlan** job for Plan \`${planId}\`.`;

      response.markdown(
        `${jobHeader}\n\n` +
          `- **Feedback:** ${feedback}\n\n` +
          (res.jobId ? `Use \`@tendril /status ${res.jobId}\` to monitor execution.` : '')
      );

      if (res.jobId) {
        await streamJobProgress(res.jobId, response, jobRunner, _token);
      }
      break;
    }

    default: {
      response.markdown(
        'Hello! I am **Tendril**, your autonomous agent planning and execution assistant.\n\n' +
          'Here are the commands you can run:\n' +
          '- `@tendril /plan <description>`: Create a new implementation plan\n' +
          '- `@tendril /run <planId>`: Execute an approved plan in an isolated worktree\n' +
          '- `@tendril /status [jobId]`: Check the status of active or specific jobs\n' +
          '- `@tendril /retry <planId> <feedback>`: Retry an executed plan with reviewer feedback\n\n' +
          'How can I help you today?'
      );
      break;
    }
  }
}

export function registerChatParticipant(
  context: vscode.ExtensionContext,
  jobRunner: IJobRunner,
  serverManager: ServerManager
): vscode.Disposable {
  if (typeof vscode.chat?.createChatParticipant !== 'function') {
    return { dispose: () => {} };
  }

  const handler: vscode.ChatRequestHandler = async (
    request: vscode.ChatRequest,
    _chatContext: vscode.ChatContext,
    response: vscode.ChatResponseStream,
    token: vscode.CancellationToken
  ) => {
    try {
      await handleChatRequest(request, response, jobRunner, serverManager, token);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      response.markdown(`An error occurred while processing your request: ${msg}`);
    }
  };

  const participant = vscode.chat.createChatParticipant(CHAT_PARTICIPANT_ID, handler);
  try {
    participant.iconPath = vscode.Uri.joinPath(context.extensionUri, 'resources', 'icon.png');
  } catch {
    // Optional icon
  }

  context.subscriptions.push(participant);
  return participant;
}
