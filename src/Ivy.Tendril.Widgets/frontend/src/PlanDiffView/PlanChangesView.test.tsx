import { afterEach, describe, it, expect, vi } from "vitest";
import { act, render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom";

import {
  PlanChangesView,
  buildFileTree,
  collapseFolderChain,
  flattenTreeOrder,
  type ChangedFile,
} from "./PlanChangesView";

function diffFor(path: string, body: string[]): string {
  return [`diff --git a/${path} b/${path}`, `--- a/${path}`, `+++ b/${path}`, ...body, ""].join("\n");
}

const files: ChangedFile[] = [
  {
    filePath: "src/App/zeta.cs",
    status: "M",
    diff: diffFor("src/App/zeta.cs", ["@@ -1 +1 @@", "-old zeta", "+new zeta"]),
    additions: 1,
    deletions: 1,
  },
  {
    filePath: "src/App/Alpha.cs",
    status: "A",
    diff: diffFor("src/App/Alpha.cs", ["@@ -0,0 +1 @@", "+alpha"]),
    additions: 1,
    deletions: 0,
  },
  {
    filePath: "README.md",
    status: "M",
    diff: diffFor("README.md", ["@@ -1 +1 @@", "-a", "+b"]),
    additions: 1,
    deletions: 1,
  },
  {
    filePath: "docs/guide/old.md",
    status: "D",
    diff: diffFor("docs/guide/old.md", ["@@ -1 +0,0 @@", "-gone"]),
    additions: 0,
    deletions: 1,
  },
];

describe("buildFileTree", () => {
  it("groups files by folder, sorts folders before files, case-insensitively", () => {
    const tree = buildFileTree(files);
    expect(tree.folders.map((f) => f.name)).toEqual(["docs", "src"]);
    expect(tree.files.map((f) => f.filePath)).toEqual(["README.md"]);
    const app = tree.folders[1].folders[0];
    expect(app.files.map((f) => f.filePath)).toEqual(["src/App/Alpha.cs", "src/App/zeta.cs"]);
  });

  it("orders names like C# OrdinalIgnoreCase, so the mobile picker and the diff pane agree", () => {
    const named = (path: string): ChangedFile => ({ filePath: path, status: "M", diff: "", additions: 0, deletions: 0 });
    const tree = buildFileTree([named("_util.cs"), named("alpha.cs"), named("Beta.cs"), named("[id].tsx")]);
    expect(tree.files.map((f) => f.filePath)).toEqual(["alpha.cs", "Beta.cs", "[id].tsx", "_util.cs"]);
  });

  it("collapses single-child folder chains into one label", () => {
    const tree = buildFileTree(files);
    const docs = tree.folders[0];
    expect(collapseFolderChain(docs)).toMatchObject({ label: "docs/guide" });
    expect(collapseFolderChain(docs).node.files[0].filePath).toBe("docs/guide/old.md");
  });

  it("flattens in tree order: folders first, then files", () => {
    expect(flattenTreeOrder(buildFileTree(files)).map((f) => f.filePath)).toEqual([
      "docs/guide/old.md",
      "src/App/Alpha.cs",
      "src/App/zeta.cs",
      "README.md",
    ]);
  });
});

describe("PlanChangesView", () => {
  afterEach(() => {
    delete (window as { ResizeObserver?: unknown }).ResizeObserver;
  });

  it("renders the tree beside one diff per file in tree order", () => {
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} />);

    const tree = screen.getByRole("tree", { name: "Changed files" });
    const rows = within(tree).getAllByRole("treeitem");
    expect(rows.map((r) => r.textContent)).toEqual([
      "docs/guide",
      "old.md-1",
      "src/App",
      "Alpha.cs+1",
      "zeta.cs+1-1",
      "README.md+1-1",
    ]);

    const diffIds = [...document.querySelectorAll(".ivy-changes-diffs .ivy-diff-file")].map((el) => el.id);
    expect(diffIds).toEqual(["docs/guide/old.md", "src/App/Alpha.cs", "src/App/zeta.cs", "README.md"]);
  });

  it("does not wrap the tree in its own border box", () => {
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} />);
    const tree = screen.getByRole("tree", { name: "Changed files" });
    expect(tree.parentElement).toHaveClass("ivy-changes-view");
    expect(tree.className).not.toMatch(/border/);
  });

  it("selects a file and scrolls its diff into view", () => {
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} />);

    fireEvent.click(screen.getByRole("treeitem", { name: /zeta\.cs/ }));

    expect(screen.getByRole("treeitem", { name: /zeta\.cs/ })).toHaveAttribute("aria-selected", "true");
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it("collapses and expands a folder", () => {
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} />);

    const folder = screen.getByRole("treeitem", { name: "src/App" });
    fireEvent.click(folder);
    expect(folder).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("treeitem", { name: /zeta\.cs/ })).toBeNull();

    fireEvent.click(folder);
    expect(screen.getByRole("treeitem", { name: /zeta\.cs/ })).toBeInTheDocument();
  });

  it("routes comments to the diff of their file and dispatches events under the widget id", () => {
    const eventHandler = vi.fn();
    render(
      <PlanChangesView
        id="pcv"
        eventHandler={eventHandler}
        files={files}
        comments={[
          { filePath: "README.md", changeKey: "I1", content: "readme note", lineNumber: 1 },
          { filePath: "src/App/Alpha.cs", changeKey: "I1", content: "alpha note", lineNumber: 1 },
        ]}
      />
    );

    const readme = document.getElementById("README.md")!;
    const alpha = document.getElementById("src/App/Alpha.cs")!;
    expect(within(readme).getByText("readme note")).toBeInTheDocument();
    expect(within(readme).queryByText("alpha note")).toBeNull();
    expect(within(alpha).getByText("alpha note")).toBeInTheDocument();

    fireEvent.click(within(readme).getByRole("button", { name: "Delete comment" }));
    expect(eventHandler).toHaveBeenCalledWith("OnDeleteComment", "pcv", [
      { filePath: "README.md", changeKey: "I1", content: "readme note", lineNumber: 1 },
    ]);
  });

  it("swaps the tree for a jump-to-file dropdown when the container is narrow", async () => {
    const callbacks: Array<(entries: Array<{ contentRect: { width: number } }>) => void> = [];
    window.ResizeObserver = class {
      constructor(cb: (entries: Array<{ contentRect: { width: number } }>) => void) {
        callbacks.push(cb);
      }
      observe() {}
      disconnect() {}
    } as unknown as typeof ResizeObserver;
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;

    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} />);
    expect(screen.getByRole("tree", { name: "Changed files" })).toBeInTheDocument();

    await act(async () => {
      callbacks.forEach((cb) => cb([{ contentRect: { width: 500 } }]));
      await new Promise((resolve) => requestAnimationFrame(resolve));
    });

    expect(screen.queryByRole("tree")).toBeNull();
    const select = screen.getByRole("combobox", { name: "Jump to file" }) as HTMLSelectElement;
    expect([...select.options].slice(1).map((o) => o.value)).toEqual([
      "docs/guide/old.md",
      "src/App/Alpha.cs",
      "src/App/zeta.cs",
      "README.md",
    ]);

    fireEvent.change(select, { target: { value: "README.md" } });
    expect(select.value).toBe("README.md");
    expect(scrollIntoView).toHaveBeenCalledTimes(1);
  });

  it("hides the tree when showTree is false but keeps the diffs", () => {
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} showTree={false} />);
    expect(screen.queryByRole("tree")).toBeNull();
    expect(document.querySelectorAll(".ivy-changes-diffs .ivy-diff-file")).toHaveLength(4);
  });

  it("draws folders with only a chevron and files with only the code icon", () => {
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={files} />);
    const folder = screen.getByRole("treeitem", { name: "src/App" });
    expect(folder.querySelectorAll("svg")).toHaveLength(1);
    expect(folder.querySelector(".ivy-changes-tree-chevron")).not.toBeNull();
    const file = screen.getByRole("treeitem", { name: /zeta\.cs/ });
    expect(file.querySelectorAll("svg")).toHaveLength(1);
    expect(file.querySelector(".ivy-changes-tree-icon")).not.toBeNull();
    expect(file.querySelector(".ivy-changes-tree-chevron")).toBeNull();
  });

  it("shows an empty state without files", () => {
    render(<PlanChangesView id="pcv" eventHandler={vi.fn()} files={[]} />);
    expect(screen.getByText("No file changes.")).toBeInTheDocument();
    expect(screen.queryByRole("tree")).toBeNull();
  });
});
