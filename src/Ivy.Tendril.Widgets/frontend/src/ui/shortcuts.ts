export const isMac = (): boolean =>
  typeof navigator !== "undefined" && /Mac|iP(hone|ad|od)/.test(navigator.platform);

export const modKeyLabel = (): string => (isMac() ? "⌘" : "Ctrl");

export const modAltKeys = (key: string): string[] =>
  isMac() ? ["⌘", "⌥", key] : ["Ctrl", "Alt", key];

export const NEW_CHAT_SHORTCUT_KEY = "A";
