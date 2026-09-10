import { describe, it, expect } from "vitest";
import { render, screen, renderHook, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import React from "react";
import { clampTipX, HoverTip, useHoverTip } from "./HoverTip";

describe("clampTipX", () => {
  it("centers horizontally when well within container bounds", () => {
    // container: 400, tip: 100, pad: 8 -> valid range [58, 342]
    expect(clampTipX(200, 100, 400)).toBe(200);
    expect(clampTipX(150, 100, 400)).toBe(150);
  });

  it("clamps to right boundary margin when x is near the right edge", () => {
    // container: 400, tip: 100, pad: 8 -> maxX = 400 - 50 - 8 = 342
    expect(clampTipX(380, 100, 400)).toBe(342);
    expect(clampTipX(343, 100, 400)).toBe(342);
  });

  it("clamps to left boundary margin when x is near the left edge", () => {
    // container: 400, tip: 100, pad: 8 -> minX = 50 + 8 = 58
    expect(clampTipX(20, 100, 400)).toBe(58);
    expect(clampTipX(57, 100, 400)).toBe(58);
  });

  it("centers when tooltip width exceeds container width", () => {
    // container: 100, tip: 120, pad: 8 -> tip + 2*pad = 136 >= 100 -> returns 100 / 2 = 50
    expect(clampTipX(80, 120, 100)).toBe(50);
    // tipWidth + pad * 2 >= containerWidth
    expect(clampTipX(10, 90, 100)).toBe(50);
  });

  it("returns x unmodified if container or tooltip dimensions are zero", () => {
    expect(clampTipX(150, 0, 400)).toBe(150);
    expect(clampTipX(150, 100, 0)).toBe(150);
    expect(clampTipX(150, -10, 400)).toBe(150);
    expect(clampTipX(150, 100, -20)).toBe(150);
  });
});

describe("HoverTip component", () => {
  it("renders nothing when tip is null", () => {
    const { container } = render(<HoverTip tip={null} />);
    expect(container.firstChild).toBeNull();
  });

  it("renders tooltip title and detail correctly when tip is provided", () => {
    const tip = {
      x: 100,
      y: 50,
      title: "May 2026",
      detail: "12 pull requests merged",
    };
    render(<HoverTip tip={tip} />);
    expect(screen.getByText("May 2026")).toBeInTheDocument();
    expect(screen.getByText("12 pull requests merged")).toBeInTheDocument();
  });
});

describe("useHoverTip hook", () => {
  it("sets coordinates and container width on showTip", () => {
    const { result } = renderHook(() => useHoverTip());

    const wrapEl = document.createElement("div");
    wrapEl.getBoundingClientRect = () => ({
      left: 100,
      top: 50,
      right: 500,
      bottom: 350,
      width: 400,
      height: 300,
      x: 100,
      y: 50,
      toJSON: () => {},
    });

    Object.defineProperty(result.current.wrapRef, "current", {
      value: wrapEl,
      writable: true,
    });

    const targetEl = document.createElement("div");
    targetEl.getBoundingClientRect = () => ({
      left: 140,
      top: 70,
      right: 160,
      bottom: 90,
      width: 20,
      height: 20,
      x: 140,
      y: 70,
      toJSON: () => {},
    });

    const mockEvent = {
      currentTarget: targetEl,
    } as unknown as React.MouseEvent<HTMLElement>;

    act(() => {
      result.current.showTip("Week 12", "3 PRs")(mockEvent);
    });

    expect(result.current.tip).toEqual({
      x: 140 + 20 / 2 - 100,
      y: 70 - 50,
      title: "Week 12",
      detail: "3 PRs",
      containerWidth: 400,
    });
  });

  it("resets tip state to null on hideTip", () => {
    const { result } = renderHook(() => useHoverTip());

    const wrapEl = document.createElement("div");
    wrapEl.getBoundingClientRect = () => ({
      left: 0,
      top: 0,
      right: 200,
      bottom: 200,
      width: 200,
      height: 200,
      x: 0,
      y: 0,
      toJSON: () => {},
    });
    Object.defineProperty(result.current.wrapRef, "current", {
      value: wrapEl,
      writable: true,
    });

    const targetEl = document.createElement("div");
    targetEl.getBoundingClientRect = () => ({
      left: 10,
      top: 10,
      right: 20,
      bottom: 20,
      width: 10,
      height: 10,
      x: 10,
      y: 10,
      toJSON: () => {},
    });

    act(() => {
      result.current.showTip("Test", "Detail")({
        currentTarget: targetEl,
      } as unknown as React.MouseEvent<HTMLElement>);
    });

    expect(result.current.tip).not.toBeNull();

    act(() => {
      result.current.hideTip();
    });

    expect(result.current.tip).toBeNull();
  });
});
