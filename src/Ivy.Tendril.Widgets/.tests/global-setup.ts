import { execSync, spawn } from "child_process";
import fs from "fs";
import https from "https";
import path from "path";
import { fileURLToPath } from "url";
import { findFreePort, getArtifactsDir, getSamplesCsproj, getSamplesProjectDir, writeServerState } from "./utils/server.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function globalSetup() {
  const artifactsDir = getArtifactsDir();
  fs.mkdirSync(path.join(artifactsDir, "logs"), { recursive: true });
  fs.mkdirSync(path.join(artifactsDir, "screenshots"), { recursive: true });

  // Build the samples project
  const samplesDir = getSamplesProjectDir();
  const csproj = getSamplesCsproj();
  console.log(`Building samples project: ${csproj}`);
  execSync(`dotnet build "${csproj}" --nologo -v q`, {
    cwd: samplesDir,
    stdio: "pipe",
    timeout: 120_000,
  });
  console.log("Build succeeded.");

  // Find free port
  const port = await findFreePort();
  console.log(`Starting server on port ${port}...`);

  // Start the server
  const backendLog = fs.openSync(path.join(artifactsDir, "logs", "backend.log"), "w");

  const proc = spawn("dotnet", ["run", "--no-build", "--", "--port", String(port)], {
    cwd: samplesDir,
    stdio: ["ignore", backendLog, backendLog],
    detached: false,
    shell: false,
  });

  if (!proc.pid) {
    throw new Error("Failed to start server process");
  }

  writeServerState({ pid: proc.pid, port });
  console.log(`Server started with PID ${proc.pid} on port ${port}`);

  // Wait for server to be ready
  const startTime = Date.now();
  const timeout = 30_000;
  const url = `https://localhost:${port}/`;

  while (Date.now() - startTime < timeout) {
    const ready = await new Promise<boolean>((resolve) => {
      const req = https.get(url, { rejectUnauthorized: false }, (res) => {
        resolve(res.statusCode !== undefined && res.statusCode < 500);
      });
      req.on("error", () => resolve(false));
      req.setTimeout(2000, () => {
        req.destroy();
        resolve(false);
      });
    });

    if (ready) {
      console.log(`Server ready after ${Date.now() - startTime}ms`);
      // Expose port for playwright config to consume
      process.env.APP_PORT = String(port);
      return;
    }

    await new Promise((r) => setTimeout(r, 500));
  }

  throw new Error(`Server did not become ready within ${timeout}ms`);
}

export default globalSetup;
