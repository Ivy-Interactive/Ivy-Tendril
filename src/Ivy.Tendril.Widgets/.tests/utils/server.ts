import net from "net";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const ARTIFACTS_DIR = path.join(__dirname, "..", "artifacts");
const STATE_FILE = path.join(ARTIFACTS_DIR, ".server-state.json");

export interface ServerState {
  pid: number;
  port: number;
}

export function getArtifactsDir(): string {
  return ARTIFACTS_DIR;
}

export function getStateFilePath(): string {
  return STATE_FILE;
}

export function getServerState(): ServerState {
  const raw = fs.readFileSync(STATE_FILE, "utf-8");
  return JSON.parse(raw) as ServerState;
}

export function getBaseUrl(): string {
  const { port } = getServerState();
  return `https://localhost:${port}`;
}

export function writeServerState(state: ServerState): void {
  fs.mkdirSync(path.dirname(STATE_FILE), { recursive: true });
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
}

export function findFreePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, "127.0.0.1", () => {
      const addr = server.address();
      if (!addr || typeof addr === "string") {
        server.close();
        reject(new Error("Could not get port"));
        return;
      }
      const port = addr.port;
      server.close(() => resolve(port));
    });
    server.on("error", reject);
  });
}

export function getSamplesProjectDir(): string {
  return path.resolve(__dirname, "..", "..", ".samples");
}

export function getSamplesCsproj(): string {
  return path.join(getSamplesProjectDir(), "Ivy.Tendril.Widgets.Samples.csproj");
}
