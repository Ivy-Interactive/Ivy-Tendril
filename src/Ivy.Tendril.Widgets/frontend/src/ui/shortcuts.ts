/** Platform-aware keyboard hints shared by the shell and the chat widgets. */

export const isMac = (): boolean =>
  typeof navigator !== "undefined" && /Mac|iP(hone|ad|od)/.test(navigator.platform);

export const modKeyLabel = (): string => (isMac() ? "⌘" : "Ctrl");

/** The keys of a Cmd+Opt (macOS) / Ctrl+Alt (elsewhere) chord, one entry per key cap. */
export const modAltKeys = (key: string): string[] =>
  isMac() ? ["⌘", "⌥", key] : ["Ctrl", "Alt", key];

/** The letter of the new-chat chord; the sidebar's Chat row binds it, the chat header shows it. */
export const NEW_CHAT_SHORTCUT_KEY = "A";
