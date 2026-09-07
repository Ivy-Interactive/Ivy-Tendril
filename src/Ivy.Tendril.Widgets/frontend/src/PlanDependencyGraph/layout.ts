export interface GraphNode {
  id: string;
  title?: string;
  status?: string;
  project?: string;
  level?: string;
  badge?: string;
}

/** `plan` depends on `dependsOn`, so `dependsOn` is laid out above (or left of) `plan`. */
export interface GraphEdge {
  plan: string;
  dependsOn: string;
}

export type Orientation = "vertical" | "horizontal";

export interface LayoutOptions {
  orientation?: Orientation;
  nodeWidth?: number;
  nodeHeight?: number;
  gapX?: number;
  gapY?: number;
  padding?: number;
}

export interface LaidOutNode {
  id: string;
  rank: number;
  order: number;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface LaidOutEdge {
  plan: string;
  dependsOn: string;
  /** True when the edge was part of a cycle and had to be ignored while ranking. */
  cyclic: boolean;
  path: string;
}

export interface GraphLayout {
  nodes: LaidOutNode[];
  edges: LaidOutEdge[];
  width: number;
  height: number;
  rankCount: number;
}

export const DEFAULT_LAYOUT: Required<LayoutOptions> = {
  orientation: "vertical",
  nodeWidth: 208,
  nodeHeight: 62,
  gapX: 28,
  gapY: 56,
  padding: 24,
};

const BARYCENTER_SWEEPS = 4;

/** Drops self references, duplicates, and edges naming a plan that is not in the node set. */
export function normalizeEdges(nodes: GraphNode[], edges: GraphEdge[]): GraphEdge[] {
  const known = new Set(nodes.map((n) => n.id));
  const seen = new Set<string>();
  const result: GraphEdge[] = [];
  for (const edge of edges) {
    if (!edge || edge.plan === edge.dependsOn) continue;
    if (!known.has(edge.plan) || !known.has(edge.dependsOn)) continue;
    const key = `${edge.dependsOn}\u0000${edge.plan}`;
    if (seen.has(key)) continue;
    seen.add(key);
    result.push({ plan: edge.plan, dependsOn: edge.dependsOn });
  }
  return result;
}

/**
 * Finds the edges that close a cycle, by depth-first search over the dependency direction.
 * A plan graph should be acyclic, but a hand-edited plan.yaml can always close a loop and the
 * widget still has to draw something.
 */
export function findCyclicEdges(nodes: GraphNode[], edges: GraphEdge[]): Set<string> {
  const children = new Map<string, string[]>();
  for (const node of nodes) children.set(node.id, []);
  for (const edge of edges) children.get(edge.dependsOn)!.push(edge.plan);

  const cyclic = new Set<string>();
  const state = new Map<string, 0 | 1 | 2>();

  const visit = (root: string) => {
    const stack: Array<{ id: string; next: number }> = [{ id: root, next: 0 }];
    state.set(root, 1);
    while (stack.length > 0) {
      const frame = stack[stack.length - 1];
      const kids = children.get(frame.id)!;
      if (frame.next >= kids.length) {
        state.set(frame.id, 2);
        stack.pop();
        continue;
      }
      const child = kids[frame.next++];
      const seen = state.get(child) ?? 0;
      if (seen === 1) {
        cyclic.add(edgeKey(frame.id, child));
      } else if (seen === 0) {
        state.set(child, 1);
        stack.push({ id: child, next: 0 });
      }
    }
  };

  for (const node of nodes) {
    if ((state.get(node.id) ?? 0) === 0) visit(node.id);
  }
  return cyclic;
}

export function edgeKey(dependsOn: string, plan: string): string {
  return `${dependsOn}\u0000${plan}`;
}

/** Longest-path ranking: a plan sits one rank below the deepest thing it depends on. */
export function rankNodes(
  nodes: GraphNode[],
  edges: GraphEdge[],
  cyclic: Set<string>
): Map<string, number> {
  const parents = new Map<string, string[]>();
  const children = new Map<string, string[]>();
  const indegree = new Map<string, number>();
  for (const node of nodes) {
    parents.set(node.id, []);
    children.set(node.id, []);
    indegree.set(node.id, 0);
  }
  for (const edge of edges) {
    if (cyclic.has(edgeKey(edge.dependsOn, edge.plan))) continue;
    parents.get(edge.plan)!.push(edge.dependsOn);
    children.get(edge.dependsOn)!.push(edge.plan);
    indegree.set(edge.plan, indegree.get(edge.plan)! + 1);
  }

  const ranks = new Map<string, number>();
  const queue = nodes.filter((n) => indegree.get(n.id) === 0).map((n) => n.id);
  for (const id of queue) ranks.set(id, 0);

  for (let i = 0; i < queue.length; i++) {
    const id = queue[i];
    for (const child of children.get(id)!) {
      const rank = Math.max(ranks.get(child) ?? 0, (ranks.get(id) ?? 0) + 1);
      ranks.set(child, rank);
      indegree.set(child, indegree.get(child)! - 1);
      if (indegree.get(child) === 0) queue.push(child);
    }
  }

  for (const node of nodes) if (!ranks.has(node.id)) ranks.set(node.id, 0);
  return ranks;
}

/**
 * Orders the nodes inside each rank with barycenter sweeps, which is what keeps edges from
 * crossing each other more than they have to.
 */
export function orderRanks(
  nodes: GraphNode[],
  edges: GraphEdge[],
  ranks: Map<string, number>
): string[][] {
  const rankCount = nodes.reduce((max, n) => Math.max(max, ranks.get(n.id)!), 0) + 1;
  const layers: string[][] = Array.from({ length: rankCount }, () => []);
  for (const node of nodes) layers[ranks.get(node.id)!].push(node.id);

  const parents = new Map<string, string[]>();
  const children = new Map<string, string[]>();
  for (const node of nodes) {
    parents.set(node.id, []);
    children.set(node.id, []);
  }
  for (const edge of edges) {
    parents.get(edge.plan)!.push(edge.dependsOn);
    children.get(edge.dependsOn)!.push(edge.plan);
  }

  const positions = new Map<string, number>();
  const reindex = () => {
    for (const layer of layers) layer.forEach((id, index) => positions.set(id, index));
  };
  reindex();

  const sweep = (layer: string[], neighbours: Map<string, string[]>) => {
    const scored = layer.map((id, index) => {
      const related = neighbours.get(id)!.filter((other) => positions.has(other));
      const barycenter =
        related.length > 0
          ? related.reduce((sum, other) => sum + positions.get(other)!, 0) / related.length
          : index;
      return { id, barycenter, index };
    });
    scored.sort((a, b) => a.barycenter - b.barycenter || a.index - b.index);
    return scored.map((entry) => entry.id);
  };

  for (let pass = 0; pass < BARYCENTER_SWEEPS; pass++) {
    for (let i = 1; i < layers.length; i++) layers[i] = sweep(layers[i], parents);
    reindex();
    for (let i = layers.length - 2; i >= 0; i--) layers[i] = sweep(layers[i], children);
    reindex();
  }

  return layers;
}

/**
 * Cross-axis coordinates. Each rank is pulled toward the average position of its neighbours and
 * then spread out again so nodes never overlap, which lines chains of plans up vertically.
 */
export function assignCrossPositions(
  layers: string[][],
  edges: GraphEdge[],
  size: number,
  gap: number
): Map<string, number> {
  const step = size + gap;
  const centers = new Map<string, number>();
  for (const layer of layers) {
    layer.forEach((id, index) => centers.set(id, index * step + size / 2));
  }

  const parents = new Map<string, string[]>();
  const children = new Map<string, string[]>();
  for (const layer of layers) {
    for (const id of layer) {
      parents.set(id, []);
      children.set(id, []);
    }
  }
  for (const edge of edges) {
    parents.get(edge.plan)?.push(edge.dependsOn);
    children.get(edge.dependsOn)?.push(edge.plan);
  }

  const relax = (layer: string[], neighbours: Map<string, string[]>) => {
    if (layer.length === 0) return;
    const desired = layer.map((id) => {
      const related = neighbours.get(id)!;
      if (related.length === 0) return centers.get(id)!;
      return related.reduce((sum, other) => sum + centers.get(other)!, 0) / related.length;
    });

    const placed = desired.slice();
    for (let i = 1; i < placed.length; i++) {
      placed[i] = Math.max(placed[i], placed[i - 1] + step);
    }
    const drift =
      desired.reduce((sum, value) => sum + value, 0) / desired.length -
      placed.reduce((sum, value) => sum + value, 0) / placed.length;
    layer.forEach((id, index) => centers.set(id, placed[index] + drift));
  };

  for (let pass = 0; pass < BARYCENTER_SWEEPS; pass++) {
    for (let i = 1; i < layers.length; i++) relax(layers[i], parents);
    for (let i = layers.length - 2; i >= 0; i--) relax(layers[i], children);
  }

  let min = Infinity;
  for (const value of centers.values()) min = Math.min(min, value);
  if (Number.isFinite(min)) {
    for (const [id, value] of centers) centers.set(id, value - min + size / 2);
  }
  return centers;
}

function edgePath(
  from: { x: number; y: number },
  to: { x: number; y: number },
  orientation: Orientation
): string {
  if (orientation === "vertical") {
    const delta = Math.max(Math.abs(to.y - from.y) * 0.45, 24);
    return `M ${from.x} ${from.y} C ${from.x} ${from.y + delta}, ${to.x} ${to.y - delta}, ${to.x} ${to.y}`;
  }
  const delta = Math.max(Math.abs(to.x - from.x) * 0.45, 24);
  return `M ${from.x} ${from.y} C ${from.x + delta} ${from.y}, ${to.x - delta} ${to.y}, ${to.x} ${to.y}`;
}

export function layoutGraph(
  nodes: GraphNode[],
  edges: GraphEdge[],
  options: LayoutOptions = {}
): GraphLayout {
  const opts = { ...DEFAULT_LAYOUT, ...options };
  const clean = normalizeEdges(nodes, edges);
  const cyclic = findCyclicEdges(nodes, clean);
  const ranks = rankNodes(nodes, clean, cyclic);
  const layers = orderRanks(nodes, clean, ranks);

  const vertical = opts.orientation === "vertical";
  const crossSize = vertical ? opts.nodeWidth : opts.nodeHeight;
  const crossGap = vertical ? opts.gapX : opts.gapY;
  const mainSize = vertical ? opts.nodeHeight : opts.nodeWidth;
  const mainGap = vertical ? opts.gapY : opts.gapX;
  const centers = assignCrossPositions(layers, clean, crossSize, crossGap);

  const laidOut = new Map<string, LaidOutNode>();
  for (const node of nodes) {
    const rank = ranks.get(node.id)!;
    const order = layers[rank].indexOf(node.id);
    const cross = centers.get(node.id) ?? crossSize / 2;
    const main = rank * (mainSize + mainGap) + mainSize / 2;
    laidOut.set(node.id, {
      id: node.id,
      rank,
      order,
      x: (vertical ? cross : main) - opts.nodeWidth / 2 + opts.padding,
      y: (vertical ? main : cross) - opts.nodeHeight / 2 + opts.padding,
      width: opts.nodeWidth,
      height: opts.nodeHeight,
    });
  }

  const anchors = (node: LaidOutNode, side: "out" | "in") =>
    vertical
      ? { x: node.x + node.width / 2, y: side === "out" ? node.y + node.height : node.y }
      : { x: side === "out" ? node.x + node.width : node.x, y: node.y + node.height / 2 };

  const drawnEdges: LaidOutEdge[] = clean.map((edge) => {
    const from = laidOut.get(edge.dependsOn)!;
    const to = laidOut.get(edge.plan)!;
    const isCyclic = cyclic.has(edgeKey(edge.dependsOn, edge.plan));
    return {
      plan: edge.plan,
      dependsOn: edge.dependsOn,
      cyclic: isCyclic,
      path: edgePath(anchors(from, "out"), anchors(to, "in"), opts.orientation),
    };
  });

  let width = 0;
  let height = 0;
  for (const node of laidOut.values()) {
    width = Math.max(width, node.x + node.width);
    height = Math.max(height, node.y + node.height);
  }

  return {
    nodes: nodes.map((node) => laidOut.get(node.id)!),
    edges: drawnEdges,
    width: width + opts.padding,
    height: height + opts.padding,
    rankCount: layers.length,
  };
}

/** Neighbour index used to highlight everything touching the hovered or selected plan. */
export function buildNeighbourIndex(edges: GraphEdge[]): Map<string, Set<string>> {
  const index = new Map<string, Set<string>>();
  const add = (a: string, b: string) => {
    if (!index.has(a)) index.set(a, new Set());
    index.get(a)!.add(b);
  };
  for (const edge of edges) {
    add(edge.plan, edge.dependsOn);
    add(edge.dependsOn, edge.plan);
  }
  return index;
}

const AVERAGE_CHAR_WIDTH = 0.56;

/** SVG text has no ellipsis, so titles are cut to the box width before rendering. */
export function truncateToWidth(text: string, maxWidth: number, fontSize: number): string {
  const budget = Math.floor(maxWidth / (fontSize * AVERAGE_CHAR_WIDTH));
  if (budget <= 1) return text.length > 1 ? "…" : text;
  if (text.length <= budget) return text;
  return `${text.slice(0, budget - 1).trimEnd()}…`;
}
