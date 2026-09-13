import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ActivityGrid } from "./ActivityGrid";

describe("ActivityGrid summary metrics", () => {
  it("does not render a summary metrics row", () => {
    const months = [
      { label: "Jan", weeks: [0, 2, 0, 1] },
      { label: "Feb", weeks: [3, 0, 0, 0] },
    ];
    const { container } = render(<ActivityGrid months={months} />);

    expect(container.querySelector(".tdb-activity-metrics")).toBeNull();
    expect(screen.queryByText("PRs merged")).not.toBeInTheDocument();
    expect(screen.queryByText("Active weeks")).not.toBeInTheDocument();
  });
});

describe("ActivityGrid monthly columns", () => {
  it("renders one column per month and one cell per non-zero week", () => {
    const months = [
      { label: "Jan", weeks: [0, 2, 0, 1] },
      { label: "Feb", weeks: [3, 0, 0, 0] },
    ];
    const { container } = render(<ActivityGrid months={months} />);

    expect(container.querySelectorAll(".tdb-activity-col")).toHaveLength(2);
    expect(container.querySelectorAll(".tdb-activity-cell")).toHaveLength(3);
  });

  it("shades the highest week at the top ramp level relative to the range", () => {
    const months = [{ label: "Jan", weeks: [1, 3] }];
    const { container } = render(<ActivityGrid months={months} />);

    const cells = container.querySelectorAll(".tdb-activity-cell");
    expect(cells).toHaveLength(2);
    const levels = Array.from(cells).map((cell) => cell.getAttribute("data-level"));
    expect(levels).toContain("4");
    expect(Number(levels[levels.indexOf("4") === 0 ? 1 : 0])).toBeLessThan(4);
  });

  it("renders the empty note when every week is zero", () => {
    render(<ActivityGrid months={[{ label: "Jan", weeks: [0, 0] }]} />);
    expect(screen.getByText("No merged pull requests yet")).toBeInTheDocument();
  });

  it("renders the empty note when months is empty", () => {
    render(<ActivityGrid months={[]} />);
    expect(screen.getByText("No merged pull requests yet")).toBeInTheDocument();
  });

  it("labels every month when there are 8 or fewer", () => {
    const months = Array.from({ length: 8 }, (_, i) => ({
      label: `M${i}`,
      weeks: [1],
    }));
    render(<ActivityGrid months={months} />);

    for (const month of months) {
      expect(screen.getByText(month.label)).toBeInTheDocument();
    }
  });

  it("labels every other month, always including the last, when more than 8 are supplied", () => {
    const months = Array.from({ length: 9 }, (_, i) => ({
      label: `M${i}`,
      weeks: [1],
    }));
    const { container } = render(<ActivityGrid months={months} />);

    const labels = Array.from(container.querySelectorAll(".tdb-activity-label")).map(
      (el) => el.textContent,
    );
    // step = 2, last month (index 8) always labelled: (9-1-8) % 2 === 0
    expect(labels[8]).toBe("M8");
    expect(labels[7]).toBe("");
    expect(labels.filter((l) => l !== "")).toHaveLength(5);
  });

  it("renders 16 monthly columns and labels with the final month labelled and titled", () => {
    const months = Array.from({ length: 16 }, (_, i) => ({
      label: `M${i + 1}`,
      weeks: [1, 2, 0, 1],
    }));
    const { container } = render(<ActivityGrid months={months} />);

    const cols = container.querySelectorAll(".tdb-activity-col");
    expect(cols).toHaveLength(16);

    const labels = container.querySelectorAll(".tdb-activity-label");
    expect(labels).toHaveLength(16);

    // Final month (index 15) is always labelled
    expect(labels[15].textContent).toBe("M16");
    expect(labels[15].getAttribute("title")).toBe("M16");
    // Penultimate month (index 14) is alternating/blank but carries title
    expect(labels[14].textContent).toBe("");
    expect(labels[14].getAttribute("title")).toBe("M15");
  });
});
