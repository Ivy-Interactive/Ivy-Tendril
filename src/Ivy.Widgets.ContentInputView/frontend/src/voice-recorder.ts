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
    console.log("[VoiceRecorder] start() initiated");
    this.options.onStatusChange("connecting");

    try {
      // Initialize AudioContext synchronously within the user gesture event handler
      // to prevent modern browsers from blocking/suspending the audio context.
      this.audioContext = new AudioContext({ sampleRate: 24000 });
      console.log("[VoiceRecorder] AudioContext created, state:", this.audioContext.state);
      if (this.audioContext.state === "suspended") {
        await this.audioContext.resume();
        console.log("[VoiceRecorder] AudioContext resumed, state:", this.audioContext.state);
      }

      this.stream = await navigator.mediaDevices.getUserMedia({
        audio: { sampleRate: 24000, channelCount: 1, echoCancellation: true },
      });
      console.log("[VoiceRecorder] Microphone stream acquired successfully");

      console.log("[VoiceRecorder] Connecting to WebSocket endpoint:", this.options.endpoint);
      this.ws = new WebSocket(this.options.endpoint);

      this.ws.onopen = () => {
        console.log("[VoiceRecorder] WebSocket open. Sending start message.");
        const startMsg: any = {
          type: "start",
          format: "pcm16",
          cleanup: this.options.cleanup !== false,
        };
        if (this.options.language) {
          startMsg.language = this.options.language;
        }
        this.ws?.send(JSON.stringify(startMsg));
      };

      this.ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          console.log("[VoiceRecorder] Received WebSocket message:", data.type, data);
          if (data.type === "ready") {
            this.options.onStatusChange("recording");
            if (this.stream && this.ws) {
              this.beginPcmCapture(this.stream, this.ws).catch((err) => {
                console.error("[VoiceRecorder] Failed to begin PCM capture:", err);
                this.options.onError(`Audio capture initialization failed: ${err}`);
                this.cleanup();
              });
            }
          } else if (data.type === "result") {
            console.log("[VoiceRecorder] Transcription result:", data.text);
            this.options.onResult(data.text);
            this.cleanup();
          } else if (data.type === "error") {
            console.error("[VoiceRecorder] Transcription server error:", data.message);
            this.options.onError(data.message);
            this.cleanup();
          }
        } catch (err) {
          console.error("[VoiceRecorder] Error parsing WebSocket message:", err);
        }
      };

      this.ws.onerror = (evt) => {
        console.error("[VoiceRecorder] WebSocket error event:", evt);
        this.options.onError("WebSocket error occurred connecting to transcription service.");
        this.cleanup();
      };

      this.ws.onclose = (evt) => {
        console.log(`[VoiceRecorder] WebSocket closed: code=${evt.code}, reason=${evt.reason || 'No reason'}, wasClean=${evt.wasClean}`);
        if (!evt.wasClean && evt.code !== 1000 && evt.code !== 1005) {
          this.options.onError(`WebSocket connection closed unexpectedly (code ${evt.code}: ${evt.reason || 'No reason given'})`);
        }
        this.cleanup();
      };
    } catch (err) {
      console.error("[VoiceRecorder] start() failed:", err);
      this.options.onError(`Microphone access denied or connection failed: ${err}`);
      this.options.onStatusChange("idle");
      if (this.audioContext) {
        this.audioContext.close().catch(() => {});
        this.audioContext = null;
      }
    }
  }

  private async beginPcmCapture(stream: MediaStream, ws: WebSocket) {
    console.log("[VoiceRecorder] beginPcmCapture initiated");
    if (!this.audioContext) {
      this.audioContext = new AudioContext({ sampleRate: 24000 });
    }
    if (this.audioContext.state === "suspended") {
      await this.audioContext.resume();
    }
    console.log("[VoiceRecorder] Loading audio worklet module...");
    await this.audioContext.audioWorklet.addModule(pcmWorkletUrl());
    console.log("[VoiceRecorder] Audio worklet module loaded successfully");

    const source = this.audioContext.createMediaStreamSource(stream);
    this.workletNode = new AudioWorkletNode(this.audioContext, "pcm-capture");

    let chunkCount = 0;
    this.workletNode.port.onmessage = (event) => {
      const msg = event.data;
      if (msg.type === "volume" && this.options.onVolumeChange) {
        this.options.onVolumeChange(msg.volume);
      } else if (msg.type === "pcm16") {
        if (ws.readyState === WebSocket.OPEN) {
          ws.send(msg.buffer);
          chunkCount++;
          if (chunkCount % 10 === 0) {
            console.log(`[VoiceRecorder] Sent ${chunkCount} PCM16 audio chunks to server`);
          }
        }
      }
    };

    source.connect(this.workletNode);
    this.workletNode.connect(this.audioContext.destination);
    console.log("[VoiceRecorder] Audio graph connected and recording started");
  }

  stop() {
    console.log("[VoiceRecorder] stop() initiated");
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      try {
        console.log("[VoiceRecorder] Sending stop signal to WebSocket");
        this.ws.send(JSON.stringify({ type: "stop" }));
      } catch (err) {
        console.error("[VoiceRecorder] Error sending stop signal:", err);
      }
      this.options.onStatusChange("processing");
    }

    if (this.workletNode) {
      console.log("[VoiceRecorder] Disconnecting AudioWorkletNode");
      this.workletNode.disconnect();
      this.workletNode = null;
    }

    if (this.audioContext) {
      console.log("[VoiceRecorder] Closing AudioContext");
      this.audioContext.close().catch(() => {});
      this.audioContext = null;
    }

    if (this.stream) {
      console.log("[VoiceRecorder] Stopping media stream tracks");
      this.stream.getTracks().forEach((track) => track.stop());
      this.stream = null;
    }
  }

  private cleanup() {
    const socket = this.ws;
    if (!socket) return;
    this.ws = null;

    console.log("[VoiceRecorder] Cleaning up session");
    this.stop();
    if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
      console.log("[VoiceRecorder] Closing WebSocket connection");
      socket.close();
    }
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
    this._volumeCounter = 0;
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || !input[0]) return true;

    const samples = input[0];
    for (let i = 0; i < samples.length; i++) {
      this._buffer.push(samples[i]);
    }

    this._volumeCounter++;
    if (this._volumeCounter >= 8) { // Update volume every ~40ms (8 * 128 samples / 24kHz = 42.6ms)
      let sum = 0;
      for (let i = 0; i < samples.length; i++) {
        sum += samples[i] * samples[i];
      }
      const rms = Math.sqrt(sum / samples.length);
      this.port.postMessage({ type: "volume", volume: rms });
      this._volumeCounter = 0;
    }

    while (this._buffer.length >= this._samplesPerChunk) {
      const chunk = this._buffer.splice(0, this._samplesPerChunk);
      const pcm16 = new Int16Array(chunk.length);
      for (let i = 0; i < chunk.length; i++) {
        const s = Math.max(-1, Math.min(1, chunk[i]));
        pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
      }
      this.port.postMessage({ type: "pcm16", buffer: pcm16.buffer }, [pcm16.buffer]);
    }

    return true;
  }
}

registerProcessor('pcm-capture', PcmCaptureProcessor);
`;
  const blob = new Blob([code], { type: "application/javascript" });
  return URL.createObjectURL(blob);
}
