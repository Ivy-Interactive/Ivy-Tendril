import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import "@testing-library/jest-dom";
import { ToolUseGroup } from "./tool-use-group";
import type { ToolUsePresentation } from "./types";

describe("ToolUseGroup", () => {
  it("renders collapsed by default", () => {
    const tools: ToolUsePresentation[] = [
      { toolUseId: "1", name: "Read", input: { file_path: "test.ts" }, result: "OK" },
      { toolUseId: "2", name: "Write", input: { file_path: "out.ts" }, result: "OK" },
      { toolUseId: "3", name: "Bash", input: { command: "echo test" }, result: "OUT" },
    ];

    render(<ToolUseGroup tools={tools} />);

    expect(screen.getByText("3 tool calls")).toBeInTheDocument();
    expect(screen.queryByText("Read")).not.toBeInTheDocument();
    expect(screen.queryByText("Write")).not.toBeInTheDocument();
    expect(screen.queryByText("OUT")).not.toBeInTheDocument();

    const header = screen.getByRole("button");
    expect(header).toHaveAttribute("aria-expanded", "false");
  });

  it("clicking the header reveals individual tool cards", async () => {
    const user = userEvent.setup();
    const tools: ToolUsePresentation[] = [
      { toolUseId: "1", name: "Read", input: { file_path: "test.ts" }, result: "OK" },
      { toolUseId: "2", name: "Write", input: { file_path: "out.ts" }, result: "OK" },
      { toolUseId: "3", name: "Bash", input: { command: "echo test" }, result: "OUT" },
    ];

    render(<ToolUseGroup tools={tools} />);

    const header = screen.getByRole("button");
    await user.click(header);

    expect(header).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("Read")).toBeInTheDocument();
    expect(screen.getByText("Write")).toBeInTheDocument();
    expect(screen.getByText("Bash")).toBeInTheDocument();

    expect(screen.queryByText("OUT")).not.toBeInTheDocument();
  });
});
