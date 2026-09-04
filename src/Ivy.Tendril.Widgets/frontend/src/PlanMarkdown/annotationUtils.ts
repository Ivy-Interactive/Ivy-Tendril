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

export const QUESTIONS_SELECTOR = ".pmv-questions";

/**
 * Whether a text node belongs to a `questions` block.
 *
 * Such text is invisible to annotation offsets entirely — not highlighted, and not counted. It has
 * to be both: a block's rendered text is not stable. Answering a question makes a Clear button
 * appear, and an option title that YAML had read as a number can start rendering; either shifts
 * every offset after the block and silently moves annotations off the words they were put on.
 * Counting only prose keeps an annotation anchored to the prose it was made against.
 */
function isInQuestions(node: Node): boolean {
  const element = node.parentElement;
  return !!element?.closest(QUESTIONS_SELECTOR);
}

export function rangeTouchesQuestions(container: HTMLElement, range: Range): boolean {
  for (const block of container.querySelectorAll(QUESTIONS_SELECTOR)) {
    if (range.intersectsNode(block)) return true;
  }
  return false;
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
    if (!isInQuestions(node)) {
      offset += node.textContent?.length ?? 0;
    }
    node = walker.nextNode();
  }

  return offset;
}

/** The text annotation offsets are measured against — prose only, questions blocks excluded. */
export function getPlainText(container: Node): string {
  let text = "";
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let node = walker.nextNode();
  while (node) {
    if (!isInQuestions(node)) {
      text += node.textContent ?? "";
    }
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

    // Callout text is skipped outright: not highlighted, and not counted toward the offsets, which
    // is what keeps an annotation anchored when a block's own rendering changes underneath it.
    // getPlainTextOffset skips it the same way, so the two agree on what an offset means.
    if (isInQuestions(node)) {
      node = walker.nextNode();
      continue;
    }

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
