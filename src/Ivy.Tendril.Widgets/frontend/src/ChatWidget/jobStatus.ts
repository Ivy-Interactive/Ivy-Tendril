import type { ChatJobDto, ChatMessageDto } from "./types";
import { formatSystemEvent } from "./systemEvents";

export const isRunningJob = (job: ChatJobDto) =>
  job.status === "Running" || job.status === "Pending" || job.status === "Queued" || job.status === "Blocked";

export const isCompletedJob = (job: ChatJobDto) => job.status === "Completed";

export const isFailedJob = (job: ChatJobDto) =>
  job.status === "Failed" || job.status === "Timeout" || job.status === "Stopped";

export type JobDisplayState = "running" | "completed" | "failed" | "unknown";

/**
 * Resolves the display state for a job by checking the live job list first, then falling back
 * to terminal messages in the history. Returns "unknown" if the job cannot be found or has an
 * unrecognized status.
 */
export function resolveJobState(
  jobId: string | undefined,
  jobs: ChatJobDto[],
  messages: ChatMessageDto[],
): JobDisplayState {
  // No job id: unknown
  if (!jobId) return "unknown";

  // Check live jobs first
  const job = jobs.find((j) => j.id === jobId);
  if (job) {
    if (isCompletedJob(job)) return "completed";
    if (isFailedJob(job)) return "failed";
    if (isRunningJob(job)) return "running";
    return "unknown";
  }

  // Fall back to terminal messages in history (job may have aged out of spawnedJobs)
  for (let i = messages.length - 1; i >= 0; i--) {
    const msg = messages[i];
    if (msg.role === "system") {
      const view = formatSystemEvent(msg.content);
      if (view.jobId === jobId && (view.kind === "completed" || view.kind === "failed")) {
        return view.kind;
      }
    }
  }

  // Job id not found anywhere
  return "unknown";
}
