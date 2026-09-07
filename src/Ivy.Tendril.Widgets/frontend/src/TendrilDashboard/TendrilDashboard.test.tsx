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
    expect(screen.getByText("Last 4 weeks")).toBeInTheDocument();
    expect(screen.getByText("Previous 4 weeks")).toBeInTheDocument();
    expect(screen.queryByText(/^Avg /)).not.toBeInTheDocument();
  });

  it("renders the 7-day average legend item and curve", () => {
    const { container } = renderDashboard(trendOf(30, 100, 4));

    expect(screen.getByText("7-day average")).toBeInTheDocument();
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

  // Monthly and weekly carry different figures: activeTrend standardizes on weekly when available, or trend.
  const monthly = trendOf(365, 120, 6);
  const weekly = trendOf(28, 45, 4);

  it("shows the 4 week cost series in dollars by default", () => {
    const { container } = renderDashboard(monthly, weekly);

    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: $45.00")).toBeInTheDocument();
    expect(screen.getByText("7-day average: $45.00")).toBeInTheDocument();
    expect(screen.getByText("Previous 4 weeks: $22.50")).toBeInTheDocument();
  });

  it("shows the 4 week plan series counted in plans", () => {
    const { container } = renderDashboard(monthly, weekly);

    fireEvent.click(screen.getByText("Total Plans"));
    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: 4 plans")).toBeInTheDocument();
    expect(screen.getByText("7-day average: 4 plans")).toBeInTheDocument();
    expect(screen.getByText("Previous 4 weeks: 2 plans")).toBeInTheDocument();
  });

  it("falls back to monthly trend if weekly trend is not provided", () => {
    const { container } = renderDashboard(monthly, null);

    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: $120.00")).toBeInTheDocument();
    expect(screen.getByText("7-day average: $120.00")).toBeInTheDocument();
    expect(screen.getByText("Previous 4 weeks: $60.00")).toBeInTheDocument();
  });

  it("hides the trend card entirely when there is no series", () => {
    const { container } = renderDashboard(null);

    expect(container.querySelector(".tdb-trend")).not.toBeInTheDocument();
    expect(screen.queryByText("7-day average")).not.toBeInTheDocument();
  });
});

describe("TendrilDashboard KPI card interactions and accessibility", () => {
  const kpis = [
    { id: "dailyPrs", label: "Avg Daily PR count", value: "1.2", delta: "+10%" },
    { id: "avgCostMonth", label: "Avg Cost/Month", value: "$450" },
    { id: "forecastMonth", label: "Forecast This Month", value: "$600" },
    { id: "avgCostPlan", label: "Avg Cost/Plan", value: "$24.50" },
  ];

  it("renders KPI cards with button accessibility attributes", () => {
    render(
      <TendrilDashboard
        id="dash"
        eventHandler={vi.fn()}
        events={["OnSelectKpi"]}
        kpis={kpis}
      />,
    );

    const buttons = screen.getAllByRole("button");
    const kpiButtons = buttons.filter((b) => b.classList.contains("tdb-kpi"));
    expect(kpiButtons).toHaveLength(4);

    kpiButtons.forEach((btn, idx) => {
      expect(btn).toHaveAttribute("tabindex", "0");
      expect(btn).toHaveAttribute(
        "aria-label",
        `View calculation breakdown for ${kpis[idx].label}`,
      );
    });
  });

  it("fires OnSelectKpi event when a KPI card is clicked", () => {
    const eventHandler = vi.fn();
    render(
      <TendrilDashboard
        id="dash"
        eventHandler={eventHandler}
        events={["OnSelectKpi"]}
        kpis={kpis}
      />,
    );

    const dailyPrsCard = screen.getByLabelText("View calculation breakdown for Avg Daily PR count");
    fireEvent.click(dailyPrsCard);

    expect(eventHandler).toHaveBeenCalledWith("OnSelectKpi", "dash", ["dailyPrs"]);

    const costPlanCard = screen.getByLabelText("View calculation breakdown for Avg Cost/Plan");
    fireEvent.click(costPlanCard);

    expect(eventHandler).toHaveBeenCalledWith("OnSelectKpi", "dash", ["avgCostPlan"]);
  });

  it("fires OnSelectKpi when Enter or Space key is pressed", () => {
    const eventHandler = vi.fn();
    render(
      <TendrilDashboard
        id="dash"
        eventHandler={eventHandler}
        events={["OnSelectKpi"]}
        kpis={kpis}
      />,
    );

    const monthCostCard = screen.getByLabelText("View calculation breakdown for Avg Cost/Month");
    fireEvent.keyDown(monthCostCard, { key: "Enter" });
    expect(eventHandler).toHaveBeenCalledWith("OnSelectKpi", "dash", ["avgCostMonth"]);

    const forecastCard = screen.getByLabelText("View calculation breakdown for Forecast This Month");
    fireEvent.keyDown(forecastCard, { key: " " });
    expect(eventHandler).toHaveBeenCalledWith("OnSelectKpi", "dash", ["forecastMonth"]);
  });

  it("falls back to standard identifier keys when kpi.id is not provided", () => {
    const eventHandler = vi.fn();
    const kpisWithoutId = [
      { label: "Avg Daily PR count", value: "1.2" },
      { label: "Avg Cost/Month", value: "$450" },
      { label: "Forecast This Month", value: "$600" },
      { label: "Avg Cost/Plan", value: "$24.50" },
    ];

    render(
      <TendrilDashboard
        id="dash"
        eventHandler={eventHandler}
        events={["OnSelectKpi"]}
        kpis={kpisWithoutId}
      />,
    );

    const card = screen.getByLabelText("View calculation breakdown for Avg Daily PR count");
    fireEvent.click(card);

    expect(eventHandler).toHaveBeenCalledWith("OnSelectKpi", "dash", ["dailyPrs"]);
  });
});
