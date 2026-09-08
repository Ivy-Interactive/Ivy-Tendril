import { useEffect } from "react";
import type { RefObject } from "react";

/** Calls `onOutside` on a mousedown outside every element in `refs`, only while `active`. */
export function useOutsideClick(
  active: boolean,
  refs: RefObject<HTMLElement | null>[],
  onOutside: () => void,
): void {
  useEffect(() => {
    if (!active) return;
    const handle = (e: MouseEvent) => {
      const target = e.target as Node;
      if (refs.some((ref) => ref.current?.contains(target))) return;
      onOutside();
    };
    document.addEventListener("mousedown", handle);
    return () => document.removeEventListener("mousedown", handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, onOutside, ...refs]);
}
