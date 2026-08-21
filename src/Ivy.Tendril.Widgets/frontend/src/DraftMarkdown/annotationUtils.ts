export interface MarkdownAnnotation {
  id: string;
  startOffset: number;
  endOffset: number;
  selectedText: string;
  comment: string;
  author?: string;
  isResolved?: boolean;
}

export function getInitials(name?: string): string {
  if (!name || !name.trim()) return "";
  const parts = name.trim().split(/\s+/);
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return (parts[0][0] + parts[1][0]).toUpperCase();
}

export function getPlainTextOffset(
  container: Node,
  targetNode: Node,
  targetOffset: number,
): number {
  let offset = 0;
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);

  let node = walker.nextNode();
  while (node) {
    if (node === targetNode) {
      return offset + targetOffset;
    }
    offset += node.textContent?.length ?? 0;
    node = walker.nextNode();
  }

  return offset;
}

export function getPlainText(container: Node): string {
  let text = "";
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let node = walker.nextNode();
  while (node) {
    text += node.textContent ?? "";
    node = walker.nextNode();
  }
  return text;
}

export function getTextNodesInRange(
  container: Node,
  startOffset: number,
  endOffset: number,
): Array<{ node: Text; start: number; end: number }> {
  const result: Array<{ node: Text; start: number; end: number }> = [];
  let currentOffset = 0;
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);

  let node = walker.nextNode();
  while (node && currentOffset < endOffset) {
    const textNode = node as Text;
    const length = textNode.textContent?.length ?? 0;
    const nodeStart = currentOffset;
    const nodeEnd = currentOffset + length;

    if (nodeEnd > startOffset && nodeStart < endOffset) {
      const start = Math.max(0, startOffset - nodeStart);
      const end = Math.min(length, endOffset - nodeStart);
      result.push({ node: textNode, start, end });
    }

    currentOffset = nodeEnd;
    node = walker.nextNode();
  }

  return result;
}

/**
 * Builds a Range spanning the given plain-text offsets within container, from
 * the first matching text node's start to the last matching text node's end.
 * Returns null when the walk yields no text nodes (e.g. an empty container).
 */
export function createRangeFromOffsets(
  container: HTMLElement,
  startOffset: number,
  endOffset: number,
): Range | null {
  const textNodes = getTextNodesInRange(container, startOffset, endOffset);
  if (textNodes.length === 0) return null;

  const first = textNodes[0];
  const last = textNodes[textNodes.length - 1];

  const range = document.createRange();
  range.setStart(first.node, first.start);
  range.setEnd(last.node, last.end);
  return range;
}

/**
 * Bounding rect for the plain-text offset range, kept separate from
 * createRangeFromOffsets so the DOM walk itself stays unit-testable under
 * jsdom, which reports every rect as zero.
 */
export function getOffsetRect(
  container: HTMLElement,
  startOffset: number,
  endOffset: number,
): DOMRect | null {
  return createRangeFromOffsets(container, startOffset, endOffset)?.getBoundingClientRect() ?? null;
}

export const getSelectionBoundingRect = getOffsetRect;

export function applyAnnotationHighlights(
  container: HTMLElement,
  annotations: MarkdownAnnotation[],
): void {
  container.querySelectorAll("mark[data-annotation-id]").forEach((mark) => {
    mark.querySelectorAll(".pmv-annotation-initials-badge").forEach((b) => b.remove());
    const parent = mark.parentNode;
    if (parent) {
      while (mark.firstChild) {
        parent.insertBefore(mark.firstChild, mark);
      }
      parent.removeChild(mark);
      parent.normalize();
    }
  });

  if (annotations.length === 0) return;

  const sorted = [...annotations].sort((a, b) => a.startOffset - b.startOffset);

  for (const annotation of sorted) {
    const textNodes = getTextNodesInRange(container, annotation.startOffset, annotation.endOffset);

    for (let i = 0; i < textNodes.length; i++) {
      const { node, start, end } = textNodes[i];
      const range = document.createRange();
      range.setStart(node, start);
      range.setEnd(node, end);

      const mark = document.createElement("mark");
      mark.dataset.annotationId = annotation.id;
      mark.className = annotation.isResolved
        ? "pmv-annotation-highlight pmv-annotation-resolved"
        : "pmv-annotation-highlight";
      const author = annotation.author?.trim();
      const statusPrefix = annotation.isResolved ? "[Resolved] " : "";
      mark.title = author
        ? `${statusPrefix}[${author}] ${annotation.comment}`
        : `${statusPrefix}${annotation.comment}`;

      range.surroundContents(mark);

      if (i === textNodes.length - 1 && author) {
        const initials = getInitials(author);
        if (initials) {
          const badge = document.createElement("span");
          badge.className = annotation.isResolved
            ? "pmv-annotation-initials-badge pmv-badge-resolved"
            : "pmv-annotation-initials-badge";
          badge.textContent = initials;
          badge.title = annotation.isResolved ? `[Resolved] ${author}` : author;
          mark.appendChild(badge);
        }
      }
    }
  }
}
