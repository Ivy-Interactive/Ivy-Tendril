export const CHAT_WIDTH_STORAGE_KEY = "tendril.plan.chatWidth";
export const MIN_CHAT_WIDTH = 320;
/** The chat never takes more than this share of the workspace; the plan stays readable beside it. */
export const MAX_CHAT_SHARE = 0.6;

export const readStoredChatWidth = (): number | null => {
  try {
    const raw = window.localStorage.getItem(CHAT_WIDTH_STORAGE_KEY);
    if (raw == null) return null;
    const value = Number(raw);
    return Number.isFinite(value) && value >= MIN_CHAT_WIDTH ? Math.round(value) : null;
  } catch {
    return null;
  }
};

export const writeStoredChatWidth = (width: number | null) => {
  try {
    if (width == null) window.localStorage.removeItem(CHAT_WIDTH_STORAGE_KEY);
    else window.localStorage.setItem(CHAT_WIDTH_STORAGE_KEY, String(Math.round(width)));
  } catch {
    /* storage unavailable: the width just does not persist */
  }
};

export const clampChatWidth = (width: number, rootWidth: number): number => {
  const max = Math.max(MIN_CHAT_WIDTH, Math.floor(rootWidth * MAX_CHAT_SHARE));
  return Math.min(Math.max(Math.round(width), MIN_CHAT_WIDTH), max);
};
