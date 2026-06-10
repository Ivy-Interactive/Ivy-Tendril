import { execSync } from "child_process";
import fs from "fs";
import { getStateFilePath } from "./utils/server.js";

async function globalTeardown() {
  const stateFile = getStateFilePath();

  if (!fs.existsSync(stateFile)) {
    console.log("No server state file found, nothing to tear down.");
    return;
  }

  const state = JSON.parse(fs.readFileSync(stateFile, "utf-8"));
  const pid = state.pid as number;

  console.log(`Killing server process tree (PID: ${pid})...`);

  try {
    execSync(`taskkill /pid ${pid} /F /T`, { stdio: "pipe" });
    console.log("Server process tree killed.");
  } catch {
    // Process may already be gone
    console.log("Process already terminated or kill failed, trying fallback...");
    try {
      execSync(
        `powershell -Command "Get-Process -Name 'Ivy.Tendril.Widgets.Samples' -ErrorAction SilentlyContinue | Stop-Process -Force"`,
        { stdio: "pipe" },
      );
    } catch {
      // Ignore — process already gone
    }
  }

  try {
    fs.unlinkSync(stateFile);
  } catch {
    // Ignore
  }
}

export default globalTeardown;
