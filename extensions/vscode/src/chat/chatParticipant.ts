import * as vscode from 'vscode';
import { IJobRunner } from '../jobs/jobRunner';
import { ServerManager } from '../server/serverManager';

export const CHAT_PARTICIPANT_ID = 'tendril.chatParticipant';

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
      break;
    }

    case 'status': {
      await serverManager.ensureServerRunning();

      if (prompt) {
        const jobId = prompt.split(/\s+/)[0];
        response.progress(`Checking status for job ${jobId}...`);
        const status = await jobRunner.getJobStatus(jobId);
        response.markdown(
          `### Job \`${status.id}\`\n\n` +
            `- **Status:** ${status.status}\n` +
            `- **Message:** ${status.message || 'No status message'}`
        );
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
