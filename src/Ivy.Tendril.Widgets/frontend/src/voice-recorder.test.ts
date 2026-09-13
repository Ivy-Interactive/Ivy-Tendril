import { describe, it, expect, vi, afterEach } from "vitest";

import {
  AUDIO_CAPTURE_UNSUPPORTED_ERROR,
  INSECURE_CONTEXT_ERROR,
  MEDIA_DEVICES_UNAVAILABLE_ERROR,
  VoiceRecorder,
  unsupportedEnvironmentError,
} from "./voice-recorder";

const stubMediaDevices = (value: unknown) => {
  Object.defineProperty(global.navigator, "mediaDevices", { configurable: true, value });
};

const workingMediaDevices = { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [] }) };

afterEach(() => {
  vi.unstubAllGlobals();
  stubMediaDevices(undefined);
});

describe("unsupportedEnvironmentError", () => {
  it("names the secure connection as the fix on an insecure non-loopback origin", () => {
    vi.stubGlobal("isSecureContext", false);
    vi.stubGlobal("location", { hostname: "192.168.1.42", href: "http://192.168.1.42:5000/" });
    stubMediaDevices(workingMediaDevices);
    vi.stubGlobal("AudioWorkletNode", class {});

    expect(unsupportedEnvironmentError()).toBe(INSECURE_CONTEXT_ERROR);
    expect(INSECURE_CONTEXT_ERROR).toContain("HTTPS");
  });

  it("does not blame the connection on an insecure loopback origin", () => {
    vi.stubGlobal("isSecureContext", false);
    vi.stubGlobal("location", { hostname: "localhost", href: "http://localhost:3000/" });
    stubMediaDevices(workingMediaDevices);
    vi.stubGlobal("AudioWorkletNode", class {});

    expect(unsupportedEnvironmentError()).toBeNull();
  });

  it("keeps the desktop-webview guidance when mediaDevices is missing in a secure context", () => {
    stubMediaDevices(undefined);
    vi.stubGlobal("AudioWorkletNode", class {});

    expect(unsupportedEnvironmentError()).toBe(MEDIA_DEVICES_UNAVAILABLE_ERROR);
    expect(MEDIA_DEVICES_UNAVAILABLE_ERROR).toContain("tendril --web");
  });

  it("reports an unsupported browser when AudioWorkletNode is absent", () => {
    stubMediaDevices(workingMediaDevices);

    expect(typeof AudioWorkletNode).toBe("undefined");
    expect(unsupportedEnvironmentError()).toBe(AUDIO_CAPTURE_UNSUPPORTED_ERROR);
  });

  it("returns null when the environment can capture audio", () => {
    stubMediaDevices(workingMediaDevices);
    vi.stubGlobal("AudioWorkletNode", class {});

    expect(unsupportedEnvironmentError()).toBeNull();
  });
});

describe("VoiceRecorder.start", () => {
  it("reports the environment error and stays idle without constructing an AudioContext", async () => {
    stubMediaDevices(undefined);
    const audioContext = vi.fn();
    vi.stubGlobal("AudioContext", audioContext);
    const webSocket = vi.fn();
    vi.stubGlobal("WebSocket", webSocket);

    const statuses: string[] = [];
    const onError = vi.fn();
    const recorder = new VoiceRecorder({
      endpoint: "ws://test",
      onStatusChange: (status) => statuses.push(status),
      onResult: vi.fn(),
      onError,
    });

    await recorder.start();

    expect(onError).toHaveBeenCalledWith(MEDIA_DEVICES_UNAVAILABLE_ERROR);
    expect(statuses).toEqual(["connecting", "idle"]);
    expect(audioContext).not.toHaveBeenCalled();
    expect(webSocket).not.toHaveBeenCalled();
  });

  it("produces zero console.log calls while still producing console.warn when DEV mode is disabled", async () => {
    const originalDev = import.meta.env.DEV;
    (import.meta.env as Record<string, unknown>).DEV = false;

    const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    const warnSpy = vi.spyOn(console, "warn").mockImplementation(() => {});

    try {
      stubMediaDevices(undefined);
      const recorder = new VoiceRecorder({
        endpoint: "ws://test",
        onStatusChange: vi.fn(),
        onResult: vi.fn(),
        onError: vi.fn(),
      });

      await recorder.start();

      expect(logSpy).not.toHaveBeenCalled();
      expect(warnSpy).toHaveBeenCalled();
    } finally {
      (import.meta.env as Record<string, unknown>).DEV = originalDev;
      logSpy.mockRestore();
      warnSpy.mockRestore();
    }
  });
});
