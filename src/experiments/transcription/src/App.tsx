import { useState, useRef, useCallback } from "react";

type Status = "idle" | "connecting" | "recording" | "processing";

interface LogEntry {
  time: string;
  direction: "sent" | "received" | "info" | "error";
  message: string;
}

export function App() {
  const [endpoint, setEndpoint] = useState("wss://tendril-api.ivy.app/transcribe/ws");
  const [language, setLanguage] = useState("");
  const [cleanup, setCleanup] = useState(true);
  const [status, setStatus] = useState<Status>("idle");
  const [result, setResult] = useState("");
  const [log, setLog] = useState<LogEntry[]>([]);

  const wsRef = useRef<WebSocket | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const workletNodeRef = useRef<AudioWorkletNode | null>(null);
  const streamRef = useRef<MediaStream | null>(null);

  const addLog = useCallback(
    (direction: LogEntry["direction"], message: string) => {
      const time = new Date().toLocaleTimeString("en-US", { hour12: false });
      setLog((prev) => [...prev, { time, direction, message }]);
    },
    []
  );

  const startRecording = useCallback(async () => {
    setResult("");
    setLog([]);

    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { sampleRate: 24000, channelCount: 1, echoCancellation: true },
      });
      streamRef.current = stream;

      setStatus("connecting");
      addLog("info", `Connecting to ${endpoint}`);

      const ws = new WebSocket(endpoint);
      wsRef.current = ws;

      ws.onopen = () => {
        const startMsg = {
          type: "start",
          ...(language && { language }),
          format: "pcm16",
          cleanup,
        };
        ws.send(JSON.stringify(startMsg));
        addLog("sent", JSON.stringify(startMsg));
      };

      ws.onmessage = (event) => {
        const data = JSON.parse(event.data);
        addLog("received", JSON.stringify(data));

        if (data.type === "ready") {
          setStatus("recording");
          beginPcmCapture(stream, ws);
        } else if (data.type === "result") {
          setResult(data.text);
          setStatus("idle");
        } else if (data.type === "error") {
          setResult(`Error: ${data.message}`);
          setStatus("idle");
        }
      };

      ws.onerror = () => {
        addLog("error", "WebSocket error");
        setStatus("idle");
      };

      ws.onclose = (event) => {
        addLog("info", `Connection closed (code: ${event.code})`);
        setStatus("idle");
      };
    } catch (err) {
      addLog("error", `Microphone access denied: ${err}`);
      setStatus("idle");
    }
  }, [endpoint, language, cleanup, addLog]);

  const beginPcmCapture = useCallback(
    async (stream: MediaStream, ws: WebSocket) => {
      const audioContext = new AudioContext({ sampleRate: 24000 });
      audioContextRef.current = audioContext;

      await audioContext.audioWorklet.addModule(pcmWorkletUrl());

      const source = audioContext.createMediaStreamSource(stream);
      const workletNode = new AudioWorkletNode(audioContext, "pcm-capture");
      workletNodeRef.current = workletNode;

      let chunkCount = 0;

      workletNode.port.onmessage = (event) => {
        const pcm16 = event.data as Int16Array;
        if (ws.readyState === WebSocket.OPEN) {
          ws.send(pcm16.buffer);
          chunkCount++;
          if (chunkCount % 10 === 0) {
            addLog("sent", `[pcm16 chunks sent: ${chunkCount}]`);
          }
        }
      };

      source.connect(workletNode);
      workletNode.connect(audioContext.destination);
      addLog("info", `Recording started (PCM16, 24kHz, mono)`);
    },
    [addLog]
  );

  const stopRecording = useCallback(() => {
    const ws = wsRef.current;
    const stream = streamRef.current;
    const audioContext = audioContextRef.current;
    const workletNode = workletNodeRef.current;

    if (workletNode) {
      workletNode.disconnect();
      workletNodeRef.current = null;
    }

    if (audioContext) {
      audioContext.close();
      audioContextRef.current = null;
    }

    if (stream) {
      stream.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    }

    if (ws && ws.readyState === WebSocket.OPEN) {
      const stopMsg = { type: "stop" };
      ws.send(JSON.stringify(stopMsg));
      addLog("sent", JSON.stringify(stopMsg));
      setStatus("processing");
    }
  }, [addLog]);

  return (
    <div style={styles.container}>
      <h1 style={styles.title}>Transcription Demo</h1>

      <div style={styles.config}>
        <label style={styles.label}>
          Endpoint
          <input
            style={styles.input}
            value={endpoint}
            onChange={(e) => setEndpoint(e.target.value)}
            disabled={status !== "idle"}
          />
        </label>
        <div style={styles.row}>
          <label style={styles.label}>
            Language
            <input
              style={styles.inputSmall}
              value={language}
              onChange={(e) => setLanguage(e.target.value)}
              placeholder="auto"
              disabled={status !== "idle"}
            />
          </label>
          <label style={styles.checkLabel}>
            <input
              type="checkbox"
              checked={cleanup}
              onChange={(e) => setCleanup(e.target.checked)}
              disabled={status !== "idle"}
            />
            Cleanup
          </label>
        </div>
      </div>

      <div style={styles.controls}>
        {status === "idle" && (
          <button style={styles.btnRecord} onClick={startRecording}>
            Record
          </button>
        )}
        {status === "connecting" && (
          <button style={styles.btnDisabled} disabled>
            Connecting...
          </button>
        )}
        {status === "recording" && (
          <button style={styles.btnStop} onClick={stopRecording}>
            Stop
          </button>
        )}
        {status === "processing" && (
          <button style={styles.btnDisabled} disabled>
            Processing...
          </button>
        )}
        <span style={styles.statusBadge} data-status={status}>
          {status}
        </span>
      </div>

      {result && (
        <div style={styles.resultBox}>
          <strong>Result:</strong>
          <p style={styles.resultText}>{result}</p>
        </div>
      )}

      <div style={styles.logBox}>
        <strong>Protocol Log</strong>
        <div style={styles.logScroll}>
          {log.map((entry, i) => (
            <div key={i} style={styles.logEntry} data-dir={entry.direction}>
              <span style={styles.logTime}>{entry.time}</span>
              <span style={styles.logDir}>{entry.direction}</span>
              <span>{entry.message}</span>
            </div>
          ))}
          {log.length === 0 && (
            <span style={styles.logPlaceholder}>Messages will appear here...</span>
          )}
        </div>
      </div>
    </div>
  );
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
    for (let i = 0; i < samples.length; i++) {
      this._buffer.push(samples[i]);
    }

    while (this._buffer.length >= this._samplesPerChunk) {
      const chunk = this._buffer.splice(0, this._samplesPerChunk);
      const pcm16 = new Int16Array(chunk.length);
      for (let i = 0; i < chunk.length; i++) {
        const s = Math.max(-1, Math.min(1, chunk[i]));
        pcm16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
      }
      this.port.postMessage(pcm16);
    }

    return true;
  }
}

registerProcessor('pcm-capture', PcmCaptureProcessor);
`;
  const blob = new Blob([code], { type: "application/javascript" });
  return URL.createObjectURL(blob);
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    maxWidth: 720,
    margin: "2rem auto",
    padding: "0 1rem",
    fontFamily: "system-ui, -apple-system, sans-serif",
  },
  title: {
    fontSize: "1.5rem",
    marginBottom: "1.5rem",
  },
  config: {
    display: "flex",
    flexDirection: "column",
    gap: "0.75rem",
    marginBottom: "1.5rem",
    padding: "1rem",
    border: "1px solid #ddd",
    borderRadius: 8,
    background: "#fafafa",
  },
  row: {
    display: "flex",
    gap: "1rem",
    alignItems: "flex-end",
    flexWrap: "wrap",
  },
  label: {
    display: "flex",
    flexDirection: "column",
    gap: "0.25rem",
    fontSize: "0.85rem",
    fontWeight: 500,
    flex: 1,
  },
  checkLabel: {
    display: "flex",
    alignItems: "center",
    gap: "0.4rem",
    fontSize: "0.85rem",
    fontWeight: 500,
    cursor: "pointer",
  },
  input: {
    padding: "0.5rem",
    border: "1px solid #ccc",
    borderRadius: 4,
    fontSize: "0.9rem",
  },
  inputSmall: {
    padding: "0.5rem",
    border: "1px solid #ccc",
    borderRadius: 4,
    fontSize: "0.9rem",
    minWidth: 80,
  },
  controls: {
    display: "flex",
    alignItems: "center",
    gap: "1rem",
    marginBottom: "1.5rem",
  },
  btnRecord: {
    padding: "0.6rem 1.5rem",
    background: "#dc2626",
    color: "#fff",
    border: "none",
    borderRadius: 6,
    fontSize: "1rem",
    cursor: "pointer",
    fontWeight: 600,
  },
  btnStop: {
    padding: "0.6rem 1.5rem",
    background: "#1d4ed8",
    color: "#fff",
    border: "none",
    borderRadius: 6,
    fontSize: "1rem",
    cursor: "pointer",
    fontWeight: 600,
  },
  btnDisabled: {
    padding: "0.6rem 1.5rem",
    background: "#9ca3af",
    color: "#fff",
    border: "none",
    borderRadius: 6,
    fontSize: "1rem",
    cursor: "not-allowed",
    fontWeight: 600,
  },
  statusBadge: {
    padding: "0.25rem 0.75rem",
    borderRadius: 12,
    fontSize: "0.8rem",
    fontWeight: 600,
    background: "#e5e7eb",
    textTransform: "uppercase",
    letterSpacing: "0.05em",
  },
  resultBox: {
    padding: "1rem",
    border: "1px solid #bbf7d0",
    borderRadius: 8,
    background: "#f0fdf4",
    marginBottom: "1.5rem",
  },
  resultText: {
    margin: "0.5rem 0 0",
    whiteSpace: "pre-wrap",
    lineHeight: 1.5,
  },
  logBox: {
    border: "1px solid #ddd",
    borderRadius: 8,
    padding: "0.75rem",
  },
  logScroll: {
    maxHeight: 300,
    overflowY: "auto",
    marginTop: "0.5rem",
    fontFamily: "monospace",
    fontSize: "0.8rem",
    display: "flex",
    flexDirection: "column",
    gap: "0.2rem",
  },
  logEntry: {
    display: "flex",
    gap: "0.5rem",
    padding: "0.2rem 0.4rem",
    borderRadius: 3,
  },
  logTime: {
    color: "#6b7280",
    minWidth: 100,
  },
  logDir: {
    fontWeight: 600,
    minWidth: 65,
    textTransform: "uppercase",
    fontSize: "0.7rem",
    alignSelf: "center",
  },
  logPlaceholder: {
    color: "#9ca3af",
    fontStyle: "italic",
  },
};
