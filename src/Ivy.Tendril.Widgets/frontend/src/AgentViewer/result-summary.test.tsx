import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ResultSummary } from "./result-summary";
import type { ResultWire } from "./types";

describe("ResultSummary", () => {
  it("renders the error text when is_success is false and error is set", () => {
    const wire: ResultWire = {
      kind: "result",
      timestamp: "2026-05-22T10:00:00Z",
      is_success: false,
      exit_code: -1,
      error: "Agent timed out: no output received for 5 minutes (idle timeout threshold exceeded).",
    };

    render(<ResultSummary wire={wire} />);

    expect(
      screen.getByText("Agent timed out: no output received for 5 minutes (idle timeout threshold exceeded).")
    ).toBeInTheDocument();
  });

  it("renders the terminated/timed-out fallback when neither error nor response is present", () => {
    const wire: ResultWire = {
      kind: "result",
      timestamp: "2026-05-22T10:00:00Z",
      is_success: false,
      exit_code: -1,
    };

    render(<ResultSummary wire={wire} />);

    expect(screen.getByText("Agent process was terminated or timed out (exit code -1).")).toBeInTheDocument();
  });

  it("renders both the error and the response when both are present", () => {
    const wire: ResultWire = {
      kind: "result",
      timestamp: "2026-05-22T10:00:00Z",
      is_success: false,
      exit_code: 1,
      error: "Agent process exited with code 1: boom",
      response: "Partial progress before the crash.",
    };

    render(<ResultSummary wire={wire} />);

    expect(screen.getByText("Agent process exited with code 1: boom")).toBeInTheDocument();
    expect(screen.getByText("Partial progress before the crash.")).toBeInTheDocument();
  });

  it("renders no error block when is_success is true and a response is present", () => {
    const wire: ResultWire = {
      kind: "result",
      timestamp: "2026-05-22T10:00:00Z",
      is_success: true,
      response: "Task completed successfully.",
    };

    render(<ResultSummary wire={wire} />);

    expect(screen.getByText("Task completed successfully.")).toBeInTheDocument();
    expect(screen.queryByText(/terminated or timed out/)).not.toBeInTheDocument();
    expect(screen.queryByText("❌ Error")).not.toBeInTheDocument();
  });
});
