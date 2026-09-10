import { vi } from "vitest";
import type { ChatSessionDto } from "./ChatWidget";

vi.mock("pdfjs-dist", () => ({ GlobalWorkerOptions: {}, getDocument: vi.fn() }));
vi.mock("pdfjs-dist/build/pdf.worker.mjs?url", () => ({ default: "" }));

export function setupChatWidgetTestEnvironment() {
  window.ResizeObserver = class {
    observe = vi.fn();
    unobserve = vi.fn();
    disconnect = vi.fn();
  } as any;
  window.HTMLElement.prototype.scrollIntoView = vi.fn();
}

export function createMockSession(overrides: Partial<ChatSessionDto> = {}): ChatSessionDto {
  return {
    id: "sess-1",
    title: "Session 1",
    agentId: "claude",
    modelId: "opus",
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    messages: [],
    ...overrides,
  };
}

export function questionsFence(...lines: string[]): string {
  return ["```questions", ...lines, "```"].join("\n");
}
