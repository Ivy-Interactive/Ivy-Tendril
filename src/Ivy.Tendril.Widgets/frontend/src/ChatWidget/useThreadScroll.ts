import { useCallback, useLayoutEffect, useRef } from "react";

const SCROLL_THRESHOLD = 50;
/** Breathing room above a pinned message, matching the thread's top padding. */
const PIN_TOP_PADDING = 10;

interface Pin {
  messageId: string;
  scrolled: boolean;
}

const escapeAttribute = (value: string): string => value.replace(/["\\]/g, "\\$&");

/**
 * Scroll state for the message list. Keeps the list following the stream while the reader is at
 * the bottom, and lets a just-sent message be pinned to the top of the viewport: a spacer after
 * the content makes that position reachable and shrinks as the reply below it grows, so the
 * message stays put until the reply is taller than the viewport, at which point the usual
 * follow-the-bottom behaviour takes over.
 */
export function useThreadScroll() {
  const containerRef = useRef<HTMLDivElement>(null);
  const spacerRef = useRef<HTMLDivElement>(null);
  const isAtBottomRef = useRef(true);
  const pinRef = useRef<Pin | null>(null);

  const checkIsAtBottom = useCallback((element: HTMLElement) => {
    const { scrollTop, scrollHeight, clientHeight } = element;
    return scrollHeight - scrollTop - clientHeight <= SCROLL_THRESHOLD;
  }, []);

  const scrollToBottom = useCallback((behavior: "auto" | "smooth" = "auto") => {
    const container = containerRef.current;
    if (!container) return;
    const targetTop = Math.max(0, container.scrollHeight - container.clientHeight);
    if (behavior === "auto" || typeof container.scrollTo !== "function") {
      container.scrollTop = targetTop;
    } else {
      container.scrollTo({ top: targetTop, behavior: "smooth" });
    }
  }, []);

  const pinMessage = useCallback((messageId: string) => {
    pinRef.current = { messageId, scrolled: false };
    isAtBottomRef.current = true;
  }, []);

  /** The pinned message was replaced by its server-side copy; keep the pin on the new row. */
  const retargetPin = useCallback((fromMessageId: string, toMessageId: string) => {
    const pin = pinRef.current;
    if (pin && pin.messageId === fromMessageId) pinRef.current = { ...pin, messageId: toMessageId };
  }, []);

  const clearPin = useCallback(() => {
    pinRef.current = null;
    const spacer = spacerRef.current;
    if (spacer) spacer.style.height = "0px";
  }, []);

  useLayoutEffect(() => {
    const container = containerRef.current;
    const spacer = spacerRef.current;
    const pin = pinRef.current;
    if (!container || !spacer || !pin) return;

    const target = container.querySelector<HTMLElement>(`[data-message-id="${escapeAttribute(pin.messageId)}"]`);
    if (!target) return;

    const contentEnd = spacer.offsetTop;
    const needed = container.clientHeight - (contentEnd - target.offsetTop) - PIN_TOP_PADDING;
    spacer.style.height = `${Math.max(0, Math.round(needed))}px`;

    if (!pin.scrolled) {
      pin.scrolled = true;
      container.scrollTop = Math.max(0, target.offsetTop - PIN_TOP_PADDING);
    }
  });

  return { containerRef, spacerRef, isAtBottomRef, checkIsAtBottom, scrollToBottom, pinMessage, retargetPin, clearPin };
}
