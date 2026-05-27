export type VoiceStatus = "idle" | "connecting" | "recording" | "processing";

export interface VoiceRecorderOptions {
  endpoint: string;
  language?: string;
  cleanup?: boolean;
  onStatusChange: (status: VoiceStatus) => void;
  onResult: (text: string) => void;
  onError: (error: string) => void;
  onVolumeChange?: (volume: number) => void;
}

export class VoiceRecorder {
  private options: VoiceRecorderOptions;
  private ws: WebSocket | null = null;
  private audioContext: AudioContext | null = null;
  private workletNode: AudioWorkletNode | null = null;
  private stream: MediaStream | null = null;

  constructor(options: VoiceRecorderOptions) {
    this.options = options;
  }

  async start() {
    this.options.onStatusChange("connecting");

    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        audio: { sampleRate: 24000, channelCount: 1, echoCancellation: true },
      });

      this.ws = new WebSocket(this.options.endpoint);

      this.ws.onopen = () => {
        const startMsg = {
          type: "start",
          ...(this.options.language && { language: this.options.language }),
          format: "pcm16",
          cleanup: this.options.cleanup !== false,
        };
        this.ws?.send(JSON.stringify(startMsg));
      };

      this.ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          if (data.type === "ready") {
            this.options.onStatusChange("recording");
            if (this.stream && this.ws) {
              this.beginPcmCapture(this.stream, this.ws).catch((err) => {
                this.options.onError(`Audio capture initialization failed: ${err}`);
                this.cleanup();
              });
            }
          } else if (data.type === "result") {
            this.options.onResult(data.text);
            this.cleanup();
          } else if (data.type === "error") {
            this.options.onError(data.message);
            this.cleanup();
          }
        } catch (err) {
          console.error("Error parsing websocket message", err);
        }
      };

      this.ws.onerror = () => {
        this.options.onError("WebSocket error occurred");
        this.cleanup();
      };

      this.ws.onclose = () => {
        this.cleanup();
      };
    } catch (err) {
      this.options.onError(`Microphone access denied: ${err}`);
      this.options.onStatusChange("idle");
    }
  }

  private async beginPcmCapture(stream: MediaStream, ws: WebSocket) {
    this.audioContext = new AudioContext({ sampleRate: 24000 });
    await this.audioContext.audioWorklet.addModule(pcmWorkletUrl());

    const source = this.audioContext.createMediaStreamSource(stream);
    this.workletNode = new AudioWorkletNode(this.audioContext, "pcm-capture");

    this.workletNode.port.onmessage = (event) => {
      const msg = event.data;
      if (msg.type === "volume" && this.options.onVolumeChange) {
        this.options.onVolumeChange(msg.volume);
      } else if (msg.type === "pcm16") {
        if (ws.readyState === WebSocket.OPEN) {
          ws.send(msg.buffer);
        }
      }
    };

    source.connect(this.workletNode);
    this.workletNode.connect(this.audioContext.destination);
  }

  stop() {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      try {
        this.ws.send(JSON.stringify({ type: "stop" }));
      } catch (err) {
        console.error("Error sending stop signal", err);
      }
      this.options.onStatusChange("processing");
    }

    if (this.workletNode) {
      this.workletNode.disconnect();
      this.workletNode = null;
    }

    if (this.audioContext) {
      this.audioContext.close().catch(() => {});
      this.audioContext = null;
    }

    if (this.stream) {
      this.stream.getTracks().forEach((track) => track.stop());
      this.stream = null;
    }
  }

  private cleanup() {
    this.stop();
    this.ws = null;
    this.options.onStatusChange("idle");
  }
}

function pcmWorkletUrl(): string {
  const code = `
class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this._buffer = [];
    this._samplesPerChunk = 2400; // 100ms at 24kHz
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || !input[0]) return true;

    const samples = input[0];
    
    let sum = 0;
    for (let i = 0; i < samples.length; i++) {
      this._buffer.push(samples[i]);
      sum += samples[i] * samples[i];
    }
    const rms = Math.sqrt(sum / samples.length);
    this.port.postMessage({ type: "volume", volume: rms });

    while (this._buffer.length >= this._samplesPerChunk) {
      const chunk = this._buffer.splice(0, this._samplesPerChunk);
      const pcm16 = new Int16Array(chunk.length);
      for (let i = 0; i < chunk.length; i++) {
        const s = Math.max(-1, Math.min(1, chunk[i]));
        pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
      }
      this.port.postMessage({ type: "pcm16", buffer: pcm16.buffer });
    }

    return true;
  }
}

registerProcessor('pcm-capture', PcmCaptureProcessor);
`;
  const blob = new Blob([code], { type: "application/javascript" });
  return URL.createObjectURL(blob);
}
