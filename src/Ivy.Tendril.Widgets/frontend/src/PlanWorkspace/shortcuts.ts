import { useEffect } from "react";
import { isEditableTarget, isMac } from "../Shell/types";

const NAMED_KEYS: Record<string, string> = {
  backspace: "⌫",
  escape: "Esc",
  enter: "↵",
  delete: "Del",
};

const MODIFIER_LABELS: Record<string, string> = {
  ctrl: "Ctrl",
  control: "Ctrl",
  meta: "⌘",
  cmd: "⌘",
  alt: isMac() ? "⌥" : "Alt",
  option: "⌥",
  shift: "⇧",
};

const parts = (shortcut: string): string[] =>
  shortcut
    .split("+")
    .map((part) => part.trim())
    .filter(Boolean);

/** The keys a tooltip or button badge shows for a shortcut, e.g. `["⌘", "K"]` or `["⌫"]`. */
export const shortcutKeys = (shortcut: string): string[] => {
  const keys = parts(shortcut);
  if (keys.length === 0) return [];
  const key = keys[keys.length - 1];
  const modifiers = keys.slice(0, -1).map((modifier) => {
    const lower = modifier.toLowerCase();
    if (lower === "mod") return isMac() ? "⌘" : "Ctrl";
    return MODIFIER_LABELS[lower] ?? modifier;
  });
  const keyLabel = key.length === 1 ? key.toUpperCase() : (NAMED_KEYS[key.toLowerCase()] ?? key);
  return [...modifiers, keyLabel];
};

/** True when the keydown is the shortcut: same key, exactly the wanted modifiers (shift is free for letters). */
export const matchesShortcut = (e: KeyboardEvent, shortcut: string): boolean => {
  const keys = parts(shortcut);
  if (keys.length === 0) return false;
  const key = keys[keys.length - 1];
  const modifiers = keys.slice(0, -1).map((modifier) => modifier.toLowerCase());
  const mac = isMac();
  const wantCtrl = modifiers.includes("ctrl") || modifiers.includes("control") || (modifiers.includes("mod") && !mac);
  const wantMeta = modifiers.includes("meta") || modifiers.includes("cmd") || (modifiers.includes("mod") && mac);
  const wantAlt = modifiers.includes("alt") || modifiers.includes("option");
  const wantShift = modifiers.includes("shift");
  if (e.ctrlKey !== wantCtrl || e.metaKey !== wantMeta || e.altKey !== wantAlt) return false;
  if (key.length === 1) {
    if (wantShift && !e.shiftKey) return false;
    return e.key.toLowerCase() === key.toLowerCase();
  }
  if (e.shiftKey !== wantShift) return false;
  return e.key.toLowerCase() === key.toLowerCase();
};

export interface ShortcutBinding {
  tag: string;
  shortcut?: string;
  disabled?: boolean;
}

/** A modal layer owned by the host (an Ivy dialog or sheet) takes the keyboard; page shortcuts stay quiet under it. */
const hostModalOpen = (): boolean =>
  !!document.querySelector('[role="dialog"][data-state="open"], [role="alertdialog"][data-state="open"]');

/**
 * Binds every action's shortcut on the document while nothing editable has focus and no dialog is
 * open, the way the framework's `ShortcutKey` did for the buttons this widget replaces.
 */
export const useActionShortcuts = (bindings: ShortcutBinding[], fire: (tag: string) => void, enabled: boolean) => {
  useEffect(() => {
    if (!enabled) return;
    const handle = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.repeat || hostModalOpen()) return;
      if (e.target instanceof Element && isEditableTarget(e)) return;
      const hit = bindings.find((binding) => binding.shortcut && !binding.disabled && matchesShortcut(e, binding.shortcut));
      if (!hit) return;
      e.preventDefault();
      fire(hit.tag);
    };
    document.addEventListener("keydown", handle);
    return () => document.removeEventListener("keydown", handle);
  }, [bindings, fire, enabled]);
};
