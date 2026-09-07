import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, fireEvent, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { TendrilDashboard } from "./TendrilDashboard";
import type { DashboardTrendDto } from "./types";

const PLOT_LEFT = 44;
const PLOT_WIDTH = 548;
const CHART_WIDTH = PLOT_LEFT + PLOT_WIDTH + 8;

/** `count` ascending days ending on 2026-09-06. */
const days = (count: number): string[] =>
  Array.from({ length: count }, (_, i) => {
    const date = new Date(Date.UTC(2026, 8, 6));
    date.setUTCDate(date.getUTCDate() - (count - 1 - i));
    return date.toISOString().slice(0, 10);
  });

/**
 * A trend payload whose cost and plan figures are far enough apart that a tooltip proves which
 * series and which unit the chart picked up.
 */
const trendOf = (
  count: number,
  cost: number,
  plans: number,
  rollingKnown = true,
): DashboardTrendDto => {
  const dates = days(count);
  return {
    dates,
    cost: dates.map(() => cost),
    plans: dates.map(() => plans),
    prevCost: dates.map(() => cost / 2),
    prevPlans: dates.map(() => plans / 2),
    rollingCost: dates.map((_, i) => (rollingKnown && i >= 6 ? cost : null)),
    rollingPlans: dates.map((_, i) => (rollingKnown && i >= 6 ? plans : null)),
  };
};

const renderDashboard = (
  trend: DashboardTrendDto | null,
  trendWeekly: DashboardTrendDto | null = null,
) =>
  render(
    <TendrilDashboard id="dash" eventHandler={vi.fn()} trend={trend} trendWeekly={trendWeekly} />,
  );

/** The chart's own svg, not the first one in the tree: every status icon is an svg too. */
const hoverLastPoint = (container: HTMLElement) =>
  fireEvent.mouseMove(container.querySelector(".tdb-chart-wrap svg")!, {
    clientX: PLOT_LEFT + PLOT_WIDTH,
    clientY: 100,
  });

describe("TendrilDashboard trend legend", () => {
  beforeEach(() => {
    Element.prototype.getBoundingClientRect = vi.fn(
      () => ({ width: CHART_WIDTH, height: 236, top: 0, left: 0, right: CHART_WIDTH, bottom: 236, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect,
    );
    globalThis.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as unknown as typeof ResizeObserver;
  });

  it("names the rolling average and drops the old constant average item", () => {
    renderDashboard(trendOf(30, 100, 4));

    expect(screen.getByText("7-day average")).toBeInTheDocument();
    expect(screen.getByText("Last 12 months")).toBeInTheDocument();
    expect(screen.getByText("Previous year")).toBeInTheDocument();
    expect(screen.queryByText(/^Avg /)).not.toBeInTheDocument();
  });

  it("explains the missing curve when no day has a full window yet", () => {
    const { container } = renderDashboard(trendOf(4, 100, 4, false));

    expect(screen.getByText("7-day average (needs 7 days of history)")).toBeInTheDocument();
    expect(container.querySelector(".tdb-legend-item-empty")).toBeInTheDocument();
    expect(container.querySelectorAll(".tdb-trend-avg-curve")).toHaveLength(0);
  });

  it("keeps the item unmuted once the curve can be drawn", () => {
    const { container } = renderDashboard(trendOf(30, 100, 4));

    expect(container.querySelector(".tdb-legend-item-empty")).not.toBeInTheDocument();
    expect(container.querySelectorAll(".tdb-trend-avg-curve")).toHaveLength(1);
  });
});

describe("TendrilDashboard range and metric combinations", () => {
  beforeEach(() => {
    Element.prototype.getBoundingClientRect = vi.fn(
      () => ({ width: CHART_WIDTH, height: 236, top: 0, left: 0, right: CHART_WIDTH, bottom: 236, x: 0, y: 0, toJSON: () => ({}) }) as DOMRect,
    );
    globalThis.ResizeObserver = class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    } as unknown as typeof ResizeObserver;
  });

  // Month and week carry different figures so a toggle that read the wrong payload would show it.
  const monthly = trendOf(365, 120, 6);
  const weekly = trendOf(28, 45, 4);

  it("shows the yearly cost series in dollars", () => {
    const { container } = renderDashboard(monthly, weekly);

    hoverLastPoint(container);

    expect(screen.getByText("Last 12 months: $120.00")).toBeInTheDocument();
    expect(screen.getByText("7-day average: $120.00")).toBeInTheDocument();
    expect(screen.getByText("Previous year: $60.00")).toBeInTheDocument();
  });

  it("shows the yearly plan series counted in plans", () => {
    const { container } = renderDashboard(monthly, weekly);

    fireEvent.click(screen.getByText("Total Plans"));
    hoverLastPoint(container);

    expect(screen.getByText("Last 12 months: 6 plans")).toBeInTheDocument();
    expect(screen.getByText("7-day average: 6 plans")).toBeInTheDocument();
    expect(screen.getByText("Previous year: 3 plans")).toBeInTheDocument();
  });

  it("shows the 4 week cost series in dollars", () => {
    const { container } = renderDashboard(monthly, weekly);

    fireEvent.click(screen.getByText("Week"));
    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: $45.00")).toBeInTheDocument();
    expect(screen.getByText("7-day average: $45.00")).toBeInTheDocument();
    expect(screen.getByText("Previous 4 weeks: $22.50")).toBeInTheDocument();
  });

  it("shows the 4 week plan series counted in plans", () => {
    const { container } = renderDashboard(monthly, weekly);

    fireEvent.click(screen.getByText("Week"));
    fireEvent.click(screen.getByText("Total Plans"));
    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: 4 plans")).toBeInTheDocument();
    expect(screen.getByText("7-day average: 4 plans")).toBeInTheDocument();
    expect(screen.getByText("Previous 4 weeks: 2 plans")).toBeInTheDocument();
  });

  it("hides the trend card entirely when there is no series", () => {
    const { container } = renderDashboard(null);

    expect(container.querySelector(".tdb-trend")).not.toBeInTheDocument();
    expect(screen.queryByText("7-day average")).not.toBeInTheDocument();
  });
});
