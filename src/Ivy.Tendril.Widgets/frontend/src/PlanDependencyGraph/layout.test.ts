import { describe, expect, it } from "vitest";
import {
  buildNeighbourIndex,
  edgeKey,
  findCyclicEdges,
  layoutGraph,
  normalizeEdges,
  rankNodes,
  truncateToWidth,
  type GraphEdge,
  type GraphNode,
} from "./layout";

const nodes = (...ids: string[]): GraphNode[] => ids.map((id) => ({ id, title: id }));
const edge = (plan: string, dependsOn: string): GraphEdge => ({ plan, dependsOn });

describe("normalizeEdges", () => {
  it("drops self references", () => {
    expect(normalizeEdges(nodes("a"), [edge("a", "a")])).toEqual([]);
  });

  it("drops edges naming a plan that is not in the graph", () => {
    expect(normalizeEdges(nodes("a"), [edge("a", "ghost")])).toEqual([]);
  });

  it("keeps one copy of a duplicated edge", () => {
    expect(normalizeEdges(nodes("a", "b"), [edge("a", "b"), edge("a", "b")])).toEqual([
      edge("a", "b"),
    ]);
  });
});

describe("rankNodes", () => {
  it("puts a plan one rank below the deepest thing it depends on", () => {
    const graph = nodes("a", "b", "c");
    const links = [edge("b", "a"), edge("c", "b"), edge("c", "a")];
    const ranks = rankNodes(graph, links, new Set());
    expect(ranks.get("a")).toBe(0);
    expect(ranks.get("b")).toBe(1);
    expect(ranks.get("c")).toBe(2);
  });

  it("ranks unconnected plans at the top", () => {
    const ranks = rankNodes(nodes("a", "b"), [], new Set());
    expect(ranks.get("a")).toBe(0);
    expect(ranks.get("b")).toBe(0);
  });
});

describe("findCyclicEdges", () => {
  it("finds nothing in an acyclic graph", () => {
    expect(findCyclicEdges(nodes("a", "b"), [edge("b", "a")]).size).toBe(0);
  });

  it("marks the edge that closes a loop", () => {
    const graph = nodes("a", "b", "c");
    const links = [edge("b", "a"), edge("c", "b"), edge("a", "c")];
    const cyclic = findCyclicEdges(graph, links);
    expect(cyclic.size).toBe(1);
    expect(cyclic.has(edgeKey("c", "a"))).toBe(true);
  });

  it("still ranks every plan when the graph has a cycle", () => {
    const graph = nodes("a", "b", "c");
    const links = [edge("b", "a"), edge("c", "b"), edge("a", "c")];
    const ranks = rankNodes(graph, links, findCyclicEdges(graph, links));
    expect([...ranks.values()].every((rank) => Number.isInteger(rank))).toBe(true);
  });
});

describe("layoutGraph", () => {
  it("stacks a dependency chain top to bottom", () => {
    const layout = layoutGraph(nodes("a", "b", "c"), [edge("b", "a"), edge("c", "b")]);
    const [a, b, c] = layout.nodes;
    expect(a.y).toBeLessThan(b.y);
    expect(b.y).toBeLessThan(c.y);
    expect(layout.rankCount).toBe(3);
  });

  it("stacks left to right when horizontal", () => {
    const layout = layoutGraph(nodes("a", "b"), [edge("b", "a")], { orientation: "horizontal" });
    const [a, b] = layout.nodes;
    expect(a.x).toBeLessThan(b.x);
    expect(a.y).toBeCloseTo(b.y, 5);
  });

  it("never overlaps two plans in the same rank", () => {
    const layout = layoutGraph(nodes("root", "a", "b", "c"), [
      edge("a", "root"),
      edge("b", "root"),
      edge("c", "root"),
    ]);
    const rankOne = layout.nodes.filter((node) => node.rank === 1).sort((l, r) => l.x - r.x);
    expect(rankOne).toHaveLength(3);
    for (let i = 1; i < rankOne.length; i++) {
      expect(rankOne[i].x).toBeGreaterThanOrEqual(rankOne[i - 1].x + rankOne[i - 1].width);
    }
  });

  it("draws one path per edge and flags the cyclic one", () => {
    const layout = layoutGraph(nodes("a", "b"), [edge("b", "a"), edge("a", "b")]);
    expect(layout.edges).toHaveLength(2);
    expect(layout.edges.every((e) => e.path.startsWith("M "))).toBe(true);
    expect(layout.edges.filter((e) => e.cyclic)).toHaveLength(1);
  });

  it("sizes the canvas around the placed plans", () => {
    const layout = layoutGraph(nodes("a", "b"), [edge("b", "a")]);
    const maxRight = Math.max(...layout.nodes.map((n) => n.x + n.width));
    const maxBottom = Math.max(...layout.nodes.map((n) => n.y + n.height));
    expect(layout.width).toBeGreaterThanOrEqual(maxRight);
    expect(layout.height).toBeGreaterThanOrEqual(maxBottom);
  });

  it("lays out an empty graph without throwing", () => {
    const layout = layoutGraph([], []);
    expect(layout.nodes).toEqual([]);
    expect(layout.edges).toEqual([]);
  });
});

describe("buildNeighbourIndex", () => {
  it("links both directions of an edge", () => {
    const index = buildNeighbourIndex([edge("b", "a")]);
    expect([...index.get("a")!]).toEqual(["b"]);
    expect([...index.get("b")!]).toEqual(["a"]);
  });
});

describe("truncateToWidth", () => {
  it("leaves a short title alone", () => {
    expect(truncateToWidth("Short", 200, 13)).toBe("Short");
  });

  it("cuts a long title and marks it with an ellipsis", () => {
    const result = truncateToWidth("A plan title far too long for the box", 60, 13);
    expect(result.endsWith("…")).toBe(true);
    expect(result.length).toBeLessThan("A plan title far too long for the box".length);
  });
});
