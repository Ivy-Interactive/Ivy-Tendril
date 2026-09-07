import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";

import { PlanDependencyGraph } from "./PlanDependencyGraph";
import type { GraphEdge, GraphNode } from "./layout";

const nodes: GraphNode[] = [
  { id: "00001", title: "Base schema", status: "Completed" },
  { id: "00002", title: "Import pipeline", status: "Executing" },
  { id: "00003", title: "Dashboard", status: "Draft" },
];

const edges: GraphEdge[] = [
  { plan: "00002", dependsOn: "00001" },
  { plan: "00003", dependsOn: "00002" },
];

const renderGraph = (props: Partial<React.ComponentProps<typeof PlanDependencyGraph>> = {}) =>
  render(<PlanDependencyGraph id="pdg-1" nodes={nodes} edges={edges} {...props} />);

const nodeFor = (title: string) => screen.getByRole("button", { name: new RegExp(title) });

describe("PlanDependencyGraph", () => {
  it("renders one clickable node per plan", () => {
    renderGraph();
    for (const node of nodes) {
      expect(nodeFor(node.title!)).toBeInTheDocument();
    }
  });

  it("emits OnNodeClick with the plan id when a node is clicked", () => {
    const eventHandler = vi.fn();
    renderGraph({ events: ["OnNodeClick"], eventHandler });

    fireEvent.click(nodeFor("Import pipeline"));

    expect(eventHandler).toHaveBeenCalledWith("OnNodeClick", "pdg-1", ["00002"]);
  });

  it("emits OnNodeClick when a focused node is activated from the keyboard", () => {
    const eventHandler = vi.fn();
    renderGraph({ events: ["OnNodeClick"], eventHandler });

    fireEvent.keyDown(nodeFor("Dashboard"), { key: "Enter" });

    expect(eventHandler).toHaveBeenCalledWith("OnNodeClick", "pdg-1", ["00003"]);
  });

  it("stays silent when the host registered no click handler", () => {
    const eventHandler = vi.fn();
    renderGraph({ events: [], eventHandler });

    fireEvent.click(nodeFor("Base schema"));

    expect(eventHandler).not.toHaveBeenCalled();
  });

  it("marks the selected plan as pressed", () => {
    renderGraph({ selectedId: "00002" });

    expect(nodeFor("Import pipeline")).toHaveAttribute("aria-pressed", "true");
    expect(nodeFor("Dashboard")).toHaveAttribute("aria-pressed", "false");
  });

  it("colors a node by its plan status", () => {
    const { container } = renderGraph();

    expect(container.querySelector('[data-plan-id="00001"]')?.getAttribute("class")).toContain(
      "pdg-status-completed"
    );
    expect(container.querySelector('[data-plan-id="00003"]')?.getAttribute("class")).toContain(
      "pdg-status-draft"
    );
  });

  it("draws an edge per dependency, pointing from the dependency to the plan", () => {
    const { container } = renderGraph();
    const drawn = [...container.querySelectorAll(".pdg-edge")].map((path) => [
      path.getAttribute("data-from"),
      path.getAttribute("data-to"),
    ]);

    expect(drawn).toEqual([
      ["00001", "00002"],
      ["00002", "00003"],
    ]);
  });

  it("dims the plans that are not connected to the hovered one", () => {
    const { container } = renderGraph();

    fireEvent.pointerEnter(nodeFor("Base schema"));

    expect(container.querySelector('[data-plan-id="00002"]')?.getAttribute("class")).not.toContain(
      "is-dimmed"
    );
    expect(container.querySelector('[data-plan-id="00003"]')?.getAttribute("class")).toContain(
      "is-dimmed"
    );
  });

  it("highlights the selection's dependencies without dimming the rest of the graph", () => {
    const { container } = renderGraph({ selectedId: "00002" });

    const classesOf = (selector: string) => container.querySelector(selector)!.getAttribute("class")!;
    expect(classesOf('[data-plan-id="00001"]')).not.toContain("is-dimmed");
    expect(classesOf('[data-from="00001"][data-to="00002"]')).toContain("is-active");
    expect(classesOf('[data-from="00002"][data-to="00003"]')).toContain("is-active");
  });

  it("shows a legend entry per status in the graph, in plan lifecycle order", () => {
    renderGraph();
    const legend = document.querySelector(".pdg-legend")!;
    expect(legend.textContent).toBe("draftexecutingcompleted");
  });

  it("shows an empty message instead of an empty canvas", () => {
    render(<PlanDependencyGraph id="pdg-1" nodes={[]} edges={[]} />);
    expect(screen.getByText("No plan dependencies to show")).toBeInTheDocument();
  });
});
