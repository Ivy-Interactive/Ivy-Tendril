export interface MarkdownAnnotation {
  id: string;
  startOffset: number;
  endOffset: number;
  selectedText: string;
  comment: string;
}

export const QUESTIONS_SELECTOR = ".pmv-questions";

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

interface TextNodeRange {
  node: Text;
  start: number;
  end: number;
}

function getTextNodesInRange(
  container: Node,
  startOffset: number,
  endOffset: number,
): TextNodeRange[] {
  const ranges: TextNodeRange[] = [];
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let offset = 0;

  let node = walker.nextNode();
  while (node) {
    const nodeLen = node.textContent?.length ?? 0;
    const nodeStart = offset;
    const nodeEnd = offset + nodeLen;

    // Callout text is excluded from highlighting, but the offset counter below
    // must still advance over it — getPlainTextOffset walks every text node in
    // the container, so skipping the offset here would shift every annotation
    // that appears after a questions block.
    const inQuestions = !!(node as Text).parentElement?.closest(QUESTIONS_SELECTOR);

    if (!inQuestions && nodeEnd > startOffset && nodeStart < endOffset) {
      const sliceStart = Math.max(0, startOffset - nodeStart);
      const sliceEnd = Math.min(nodeLen, endOffset - nodeStart);
      const slice = node.textContent?.slice(sliceStart, sliceEnd) ?? "";
      if (slice.trim().length > 0) {
        ranges.push({
          node: node as Text,
          start: sliceStart,
          end: sliceEnd,
        });
      }
    }

    if (nodeStart >= endOffset) break;
    offset = nodeEnd;
    node = walker.nextNode();
  }

  return ranges;
}

export function applyAnnotationHighlights(
  container: HTMLElement,
  annotations: MarkdownAnnotation[],
): void {
  container.querySelectorAll("mark[data-annotation-id]").forEach((mark) => {
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

    for (const { node, start, end } of textNodes) {
      const range = document.createRange();
      range.setStart(node, start);
      range.setEnd(node, end);

      const mark = document.createElement("mark");
      mark.dataset.annotationId = annotation.id;
      mark.className = "pmv-annotation-highlight";
      mark.title = annotation.comment;

      range.surroundContents(mark);
    }
  }
}
