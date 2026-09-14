import * as vscode from 'vscode';
import { COMMANDS } from '../constants';
import { IJobRunner } from '../jobs/jobRunner';
import { ServerManager } from '../server/serverManager';

export function registerPlanCommands(
  context: vscode.ExtensionContext,
  jobRunner: IJobRunner,
  serverManager: ServerManager
): void {
  // 1. Create Plan
  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.createPlan, async () => {
      const description = await vscode.window.showInputBox({
        title: 'Tendril: Create Plan',
        prompt: 'Enter a description for the new plan',
        placeHolder: 'e.g. Add dark mode support to settings app'
      });

      if (!description || description.trim().length === 0) {
        return;
      }

      let selectedProject: string | undefined;
      const projects = await jobRunner.listProjects();

      if (projects.length === 1) {
        selectedProject = projects[0];
      } else if (projects.length > 1) {
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (workspaceFolders && workspaceFolders.length > 0) {
          const wsName = workspaceFolders[0].name.toLowerCase();
          const matched = projects.find(p => p.toLowerCase() === wsName);
          if (matched) {
            selectedProject = matched;
          }
        }

        if (!selectedProject) {
          selectedProject = await vscode.window.showQuickPick(projects, {
            title: 'Select Project for Plan',
            placeHolder: 'Choose a Tendril project'
          });
        }
      }

      if (!selectedProject) {
        selectedProject = 'default';
      }

      try {
        await serverManager.ensureServerRunning();
        const res = await jobRunner.startCreatePlan(description.trim(), selectedProject);
        const jobMsg = res.jobId ? ` (Job ${res.jobId})` : '';
        vscode.window.showInformationMessage(
          `Started CreatePlan${jobMsg} for project "${selectedProject}".`
        );
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to start CreatePlan: ${msg}`);
      }
    })
  );

  // 2. Execute Plan
  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.executePlan, async (planIdArg?: string) => {
      let planId = planIdArg;
      if (!planId || typeof planId !== 'string') {
        planId = await vscode.window.showInputBox({
          title: 'Tendril: Execute Plan',
          prompt: 'Enter Plan ID to execute',
          placeHolder: 'e.g. 00399'
        });
      }

      if (!planId || planId.trim().length === 0) {
        return;
      }

      try {
        await serverManager.ensureServerRunning();
        const res = await jobRunner.startExecutePlan(planId.trim());
        const jobMsg = res.jobId ? ` (Job ${res.jobId})` : '';
        vscode.window.showInformationMessage(
          `Started ExecutePlan${jobMsg} for plan ${planId.trim()}.`
        );
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to start ExecutePlan: ${msg}`);
      }
    })
  );

  // 3. Retry Plan
  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.retryPlan, async (planIdArg?: string) => {
      let planId = planIdArg;
      if (!planId || typeof planId !== 'string') {
        planId = await vscode.window.showInputBox({
          title: 'Tendril: Retry Plan',
          prompt: 'Enter Plan ID to retry',
          placeHolder: 'e.g. 00399'
        });
      }

      if (!planId || planId.trim().length === 0) {
        return;
      }

      const feedback = await vscode.window.showInputBox({
        title: 'Tendril: Retry Plan',
        prompt: 'Enter reviewer feedback or change request',
        placeHolder: 'e.g. Fix failing unit tests and update documentation'
      });

      if (!feedback || feedback.trim().length === 0) {
        return;
      }

      try {
        await serverManager.ensureServerRunning();
        const res = await jobRunner.startRetryPlan(planId.trim(), feedback.trim());
        const jobMsg = res.jobId ? ` (Job ${res.jobId})` : '';
        vscode.window.showInformationMessage(
          `Started RetryPlan${jobMsg} for plan ${planId.trim()}.`
        );
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to start RetryPlan: ${msg}`);
      }
    })
  );

  // 4. Check Job Status
  context.subscriptions.push(
    vscode.commands.registerCommand(COMMANDS.checkJobStatus, async (jobIdArg?: string) => {
      try {
        let jobId = jobIdArg;
        if (!jobId || typeof jobId !== 'string') {
          const jobs = await jobRunner.listJobs();
          if (jobs.length > 0) {
            const items = jobs.map(j => ({
              label: `${j.id} (${j.type}) - ${j.status}`,
              description: j.planId ? `Plan ${j.planId}` : j.project,
              detail: j.message || 'No status message',
              jobId: j.id
            }));
            const picked = await vscode.window.showQuickPick(items, {
              title: 'Select Job to Check Status',
              placeHolder: 'Choose a job to view status'
            });
            if (!picked) {
              return;
            }
            jobId = picked.jobId;
          } else {
            jobId = await vscode.window.showInputBox({
              title: 'Tendril: Check Job Status',
              prompt: 'Enter Job ID',
              placeHolder: 'e.g. 01864'
            });
          }
        }

        if (!jobId || jobId.trim().length === 0) {
          return;
        }

        const status = await jobRunner.getJobStatus(jobId.trim());
        const detail = status.message ? `: ${status.message}` : '';
        vscode.window.showInformationMessage(`Job ${status.id} is ${status.status}${detail}`);
      } catch (err: unknown) {
        const msg = err instanceof Error ? err.message : String(err);
        vscode.window.showErrorMessage(`Failed to check job status: ${msg}`);
      }
    })
  );
}
