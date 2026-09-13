import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { DerivedStatus } from "./status";
import { STATUS_HOLD_MS, useHeldStatus } from "./useHeldStatus";

const tool = (text: string): DerivedStatus => ({ text, complete: false, tool: true });
const thinking: DerivedStatus = { text: "Thinking…", complete: false };
const completed: DerivedStatus = { text: "Completed", complete: true };

describe("useHeldStatus", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("passes an active tool status straight through", () => {
    const { result } = renderHook(({ s }) => useHeldStatus(s), {
      initialProps: { s: tool("Reading a.ts") },
    });
    expect(result.current.text).toBe("Reading a.ts");
  });

  it("keeps the tool label for the hold window after the tool finishes", () => {
    const { result, rerender } = renderHook(({ s }) => useHeldStatus(s), {
      initialProps: { s: tool("Reading a.ts") },
    });

    rerender({ s: thinking });
    expect(result.current.text).toBe("Reading a.ts");

    act(() => void vi.advanceTimersByTime(STATUS_HOLD_MS - 1));
    expect(result.current.text).toBe("Reading a.ts");
  });

  it("falls back to the real status once the hold expires", () => {
    const { result, rerender } = renderHook(({ s }) => useHeldStatus(s), {
      initialProps: { s: tool("Reading a.ts") },
    });

    rerender({ s: thinking });
    act(() => void vi.advanceTimersByTime(STATUS_HOLD_MS));
    expect(result.current.text).toBe("Thinking…");
  });

  it("shows a new tool call immediately, without waiting out the hold", () => {
    const { result, rerender } = renderHook(({ s }) => useHeldStatus(s), {
      initialProps: { s: tool("Reading a.ts") },
    });

    rerender({ s: thinking });
    act(() => void vi.advanceTimersByTime(500));
    rerender({ s: tool("Running command") });
    expect(result.current.text).toBe("Running command");
  });

  it("does not extend the hold when the stream updates inside the window", () => {
    const { result, rerender } = renderHook(({ s }) => useHeldStatus(s), {
      initialProps: { s: tool("Reading a.ts") },
    });

    rerender({ s: thinking });
    act(() => void vi.advanceTimersByTime(STATUS_HOLD_MS - 100));
    /* A fresh object for the same state, as each stream update produces. */
    rerender({ s: { ...thinking } });
    act(() => void vi.advanceTimersByTime(100));
    expect(result.current.text).toBe("Thinking…");
  });

  it("drops the hold as soon as the run completes", () => {
    const { result, rerender } = renderHook(({ s }) => useHeldStatus(s), {
      initialProps: { s: tool("Reading a.ts") },
    });

    rerender({ s: completed });
    expect(result.current.text).toBe("Completed");
    expect(result.current.complete).toBe(true);
  });
});
