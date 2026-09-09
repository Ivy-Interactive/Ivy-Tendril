import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { debugLog, isDebugLoggingEnabled } from "./debug-log";

describe("debug-log", () => {
  const originalDev = import.meta.env.DEV;

  beforeEach(() => {
    (import.meta.env as Record<string, unknown>).DEV = true;
    vi.restoreAllMocks();
  });

  afterEach(() => {
    (import.meta.env as Record<string, unknown>).DEV = originalDev;
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("isDebugLoggingEnabled returns boolean indicating whether import.meta.env.DEV is true", () => {
    (import.meta.env as Record<string, unknown>).DEV = true;
    expect(isDebugLoggingEnabled()).toBe(true);

    (import.meta.env as Record<string, unknown>).DEV = false;
    expect(isDebugLoggingEnabled()).toBe(false);
  });

  it("debugLog calls console.log with the given args when import.meta.env.DEV is true", () => {
    const consoleSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    (import.meta.env as Record<string, unknown>).DEV = true;

    debugLog("hello", 123, { a: 1 });
    expect(consoleSpy).toHaveBeenCalledWith("hello", 123, { a: 1 });
  });

  it("debugLog does not touch console.log when import.meta.env.DEV is falsy", () => {
    const consoleSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    (import.meta.env as Record<string, unknown>).DEV = false;

    debugLog("suppressed", "log");
    expect(consoleSpy).not.toHaveBeenCalled();
  });
});
