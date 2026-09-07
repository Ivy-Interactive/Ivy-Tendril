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
