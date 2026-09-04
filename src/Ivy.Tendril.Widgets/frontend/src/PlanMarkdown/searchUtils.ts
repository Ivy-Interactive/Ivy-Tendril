export function clearSearchHighlights(root: HTMLElement): void {
  const highlights = Array.from(root.querySelectorAll<HTMLElement>("mark.pmv-search-highlight"));
  const parentsToNormalize = new Set<Node>();
  for (const mark of highlights) {
    const parent = mark.parentNode;
    if (!parent) continue;
    parentsToNormalize.add(parent);
    while (mark.firstChild) {
      parent.insertBefore(mark.firstChild, mark);
    }
    parent.removeChild(mark);
  }
  for (const parent of parentsToNormalize) {
    parent.normalize();
  }
}

export function applySearchHighlights(root: HTMLElement, query: string): HTMLElement[] {
  clearSearchHighlights(root);
  if (!query || !query.trim()) {
    return [];
  }

  const textNodes: Text[] = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      const parent = node.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      if (parent.closest(".pmv-search-overlay, .pmv-popover, .pmv-selection-toolbar, script, style")) {
        return NodeFilter.FILTER_REJECT;
      }
      return NodeFilter.FILTER_ACCEPT;
    },
  });

  let currentNode = walker.nextNode();
  while (currentNode) {
    textNodes.push(currentNode as Text);
    currentNode = walker.nextNode();
  }

  const createdMarks: HTMLElement[] = [];
  const lowerQuery = query.toLowerCase();

  for (const textNode of textNodes) {
    const text = textNode.nodeValue || "";
    const lowerText = text.toLowerCase();
    if (!lowerText.includes(lowerQuery)) continue;

    const fragment = document.createDocumentFragment();
    let lastIndex = 0;
    let matchIndex: number;

    while ((matchIndex = lowerText.indexOf(lowerQuery, lastIndex)) !== -1) {
      if (matchIndex > lastIndex) {
        fragment.appendChild(document.createTextNode(text.substring(lastIndex, matchIndex)));
      }
      const mark = document.createElement("mark");
      mark.className = "pmv-search-highlight";
      mark.textContent = text.substring(matchIndex, matchIndex + query.length);
      fragment.appendChild(mark);
      createdMarks.push(mark);
      lastIndex = matchIndex + query.length;
    }

    if (lastIndex < text.length) {
      fragment.appendChild(document.createTextNode(text.substring(lastIndex)));
    }

    textNode.parentNode?.replaceChild(fragment, textNode);
  }

  return createdMarks;
}
