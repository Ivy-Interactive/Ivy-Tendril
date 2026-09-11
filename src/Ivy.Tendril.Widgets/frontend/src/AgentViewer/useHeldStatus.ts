import { useEffect, useState } from "react";
import type { DerivedStatus } from "./status";

/** How long a finished tool's label stays up while waiting for whatever comes next. */
export const STATUS_HOLD_MS = 2000;

/**
 * Smooths the gap between two tool calls. A fast tool leaves the run with nothing to report for a
 * moment, so the status falls back to "Thinking…" and snaps straight back to a tool label, which
 * reads as a flicker. Holding the tool label for {@link STATUS_HOLD_MS} covers that gap: another
 * tool call replaces it immediately, and a run that really has moved on to thinking says so once
 * the hold expires.
 */
export function useHeldStatus(status: DerivedStatus): DerivedStatus {
  const active = status.tool === true;
  const done = status.complete;
  const label = active ? status.text : null;
  const [held, setHeld] = useState<string | null>(null);

  useEffect(() => {
    if (label !== null) setHeld(label);
  }, [label]);

  useEffect(() => {
    if (done) setHeld(null);
  }, [done]);

  /* The window is measured from the tool ending, so stream updates inside it do not extend it. */
  useEffect(() => {
    if (active || done || held === null) return;
    const timer = setTimeout(() => setHeld(null), STATUS_HOLD_MS);
    return () => clearTimeout(timer);
  }, [active, done, held]);

  if (active || done || held === null) return status;
  return { text: held, complete: false, tool: true };
}
