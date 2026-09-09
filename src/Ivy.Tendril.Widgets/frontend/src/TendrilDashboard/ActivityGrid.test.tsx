import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ActivityGrid } from "./ActivityGrid";

describe("ActivityGrid summary metrics", () => {
  it("renders total merged PRs and active weeks", () => {
    const months = [
      { label: "Jan", weeks: [0, 2, 0, 1] },
      { label: "Feb", weeks: [3, 0, 0, 0] },
    ];
    render(<ActivityGrid months={months} />);

    expect(screen.getByText("6")).toBeInTheDocument();
    expect(screen.getByText("PRs merged")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("Active weeks")).toBeInTheDocument();
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
});
