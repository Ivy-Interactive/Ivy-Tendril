import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, fireEvent, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { TrendChart } from "./TrendChart";

const PLOT_LEFT = 44;
const PLOT_WIDTH = 548;
const CHART_WIDTH = PLOT_LEFT + PLOT_WIDTH + 8;

/** `count` ascending days ending on 2026-09-06, as the `yyyy-MM-dd` strings the chart plots. */
const days = (count: number): string[] =>
  Array.from({ length: count }, (_, i) => {
    const date = new Date(Date.UTC(2026, 8, 6));
    date.setUTCDate(date.getUTCDate() - (count - 1 - i));
    return date.toISOString().slice(0, 10);
  });

const formatCurrencyValue = (value: number): string => `$${value.toFixed(2)}`;

const renderChart = (props: Partial<React.ComponentProps<typeof TrendChart>> = {}) => {
  const dates = props.dates ?? days(30);
  return render(
    <TrendChart
      dates={dates}
      values={props.values ?? dates.map((_, i) => 100 + i * 10)}
      previous={props.previous}
      rolling={props.rolling}
      currentName={props.currentName ?? "Last 4 weeks"}
      previousName={props.previousName ?? "Previous 4 weeks"}
      formatTick={props.formatTick ?? ((v) => `$${v}`)}
      formatValue={props.formatValue ?? formatCurrencyValue}
    />,
  );
};

describe("TrendChart rolling average curve", () => {
  beforeEach(() => {
    // The SVG is only rendered once the wrapper has been measured, and jsdom measures everything
    // as zero.
    Element.prototype.getBoundingClientRect = vi.fn(
      () => ({ width: CHART_WIDTH, height: 236, top: 0, left: 0, right: CHART_WIDTH, bottom: 236, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect,
    );
    globalThis.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as unknown as typeof ResizeObserver;
  });

  it("draws one curve for a fully covered series and no horizontal reference line", () => {
    const dates = days(30);
    const values = dates.map((_, i) => 100 + (i % 11) * 10);
    const rolling = values.map((_, i) => 100 + i);

    const { container } = renderChart({ dates, values, rolling });

    expect(container.querySelectorAll(".tdb-trend-avg-curve")).toHaveLength(1);
    expect(container.querySelectorAll(".tdb-trend-avg-line")).toHaveLength(0);
  });

  it("draws an expanding average curve when rolling series is omitted", () => {
    const dates = days(14);
    const values = dates.map((_, i) => 100 + i * 10);

    const { container } = renderChart({ dates, values, rolling: undefined });

    expect(container.querySelectorAll(".tdb-trend-avg-curve")).toHaveLength(1);
  });

  it("breaks the curve into a segment per contiguous run of history", () => {
    // Leading nulls (no full window yet) and a gap in the middle must not be bridged by a line
    // through days the average was never computed for.
    const dates = days(20);
    const rolling: (number | null)[] = dates.map((_, i) =>
      i < 6 || i === 12 || i === 13 ? null : 200 + i,
    );

    const { container } = renderChart({ dates, rolling });

    expect(container.querySelectorAll(".tdb-trend-avg-curve")).toHaveLength(2);
  });

  it("draws nothing when the whole rolling series is unknown", () => {
    const dates = days(10);

    const { container } = renderChart({ dates, rolling: dates.map(() => null) });

    expect(container.querySelectorAll(".tdb-trend-avg-curve")).toHaveLength(0);
  });

  it("names the hovered day in full and reports its 7-day average", () => {
    const dates = days(30);
    const values = dates.map(() => 50);
    const rolling = dates.map((_, i) => (i < 6 ? null : 42));

    const { container } = renderChart({ dates, values, rolling });

    fireEvent.mouseMove(container.querySelector("svg")!, {
      clientX: PLOT_LEFT + PLOT_WIDTH,
      clientY: 100,
    });

    expect(screen.getByText("Sun, Sep 6, 2026")).toBeInTheDocument();
    expect(screen.getByText(/7-day average: \$42\.00/)).toBeInTheDocument();
  });

  it("omits the average row on a day whose window has no history", () => {
    const dates = days(30);
    const rolling = dates.map((_, i) => (i < 6 ? null : 42));

    const { container } = renderChart({ dates, rolling });

    fireEvent.mouseMove(container.querySelector("svg")!, { clientX: PLOT_LEFT, clientY: 100 });

    expect(screen.getByText("Sat, Aug 8, 2026")).toBeInTheDocument();
    expect(screen.queryByText(/7-day average/)).not.toBeInTheDocument();
  });
});
