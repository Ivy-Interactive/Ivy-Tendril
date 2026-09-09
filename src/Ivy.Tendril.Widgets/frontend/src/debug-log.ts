/// <reference types="vite/client" />

export function isDebugLoggingEnabled(): boolean {
  return Boolean(import.meta.env.DEV);
}

export function debugLog(...args: unknown[]): void {
  if (isDebugLoggingEnabled()) {
    console.log(...args);
  }
}
