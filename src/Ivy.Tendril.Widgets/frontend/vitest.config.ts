import { defineConfig } from "vite-plus";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    // Node enables experimental Web Storage by default from v25, so globalThis.localStorage
    // exists as an accessor evaluating to undefined without --localstorage-file. Vitest's
    // jsdom environment skips any window key that is already a global, so jsdom's real
    // Storage never lands (and window.localStorage is undefined too, since Vitest aliases
    // window to globalThis). Turning Node's webstorage off gives jsdom ownership again.
    // No-op on Node 24 and earlier, which have no webstorage globals.
    execArgv: ["--no-experimental-webstorage"],
  },
});
