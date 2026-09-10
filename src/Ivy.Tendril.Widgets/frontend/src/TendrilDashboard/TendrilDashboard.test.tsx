import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, fireEvent, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { TendrilDashboard } from "./TendrilDashboard";
import type { DashboardMonthValueDto, DashboardTrendDto } from "./types";

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

  it("names the rolling average and omits the comparison series", () => {
    renderDashboard(trendOf(30, 100, 4));

    expect(screen.getByText("7-day average")).toBeInTheDocument();
    expect(screen.getByText("Last 4 weeks")).toBeInTheDocument();
    expect(screen.queryByText("Previous 4 weeks")).not.toBeInTheDocument();
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
    expect(screen.queryByText("Previous 4 weeks: $22.50")).not.toBeInTheDocument();
  });

  it("shows the 4 week plan series counted in plans", () => {
    const { container } = renderDashboard(monthly, weekly);

    fireEvent.click(screen.getByText("Total Plans"));
    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: 4 plans")).toBeInTheDocument();
    expect(screen.getByText("7-day average: 4 plans")).toBeInTheDocument();
    expect(screen.queryByText("Previous 4 weeks: 2 plans")).not.toBeInTheDocument();
  });

  it("falls back to monthly trend if weekly trend is not provided", () => {
    const { container } = renderDashboard(monthly, null);

    hoverLastPoint(container);

    expect(screen.getByText("Last 4 weeks: $120.00")).toBeInTheDocument();
    expect(screen.getByText("7-day average: $120.00")).toBeInTheDocument();
    expect(screen.queryByText("Previous 4 weeks: $60.00")).not.toBeInTheDocument();
  });

  it("hides the trend card entirely when there is no series", () => {
    const { container } = renderDashboard(null);

    expect(container.querySelector(".tdb-trend")).not.toBeInTheDocument();
    expect(screen.queryByText("7-day average")).not.toBeInTheDocument();
  });
});

describe("TendrilDashboard git activity and pull requests side cards", () => {
  it("renders Git Activity and Pull Requests as two separate cards, not as tabs", () => {
    const { container } = render(
      <TendrilDashboard
        id="dash"
        eventHandler={vi.fn()}
        activity={[{ label: "Jan", weeks: [1] }]}
        pullRequests={[{ label: "Jan", value: 3 }]}
      />,
    );

    const sideBlocks = container.querySelectorAll(".tdb-col-side .tdb-side-block");
    const titles = Array.from(sideBlocks).map(
      (block) => block.querySelector(".tdb-block-title")?.textContent,
    );
    expect(titles).toContain("Git Activity");
    expect(titles).toContain("Pull Requests");

    expect(screen.queryByRole("button", { name: "Git Activity" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Pull Requests" })).not.toBeInTheDocument();
  });

  it("shows both the activity grid and the pull requests list at the same time", () => {
    const { container } = render(
      <TendrilDashboard
        id="dash"
        eventHandler={vi.fn()}
        activity={[{ label: "Jan", weeks: [1] }]}
        pullRequests={[{ label: "Jan", value: 3 }]}
      />,
    );

    expect(container.querySelector(".tdb-activity-col")).toBeInTheDocument();
    expect(container.querySelector(".tdb-bars")).toBeInTheDocument();
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

describe("TendrilDashboard pull requests week/month toggle", () => {
  const monthlyPrs: DashboardMonthValueDto[] = [
    { label: "Apr", value: 10 },
    { label: "May", value: 15 },
    { label: "Jun", value: 8 },
    { label: "Jul", value: 12 },
    { label: "Aug", value: 20 },
    { label: "Sep", value: 7 },
  ];

  const weeklyPrs: DashboardMonthValueDto[] = [
    { label: "Aug 3", value: 4 },
    { label: "Aug 10", value: 6 },
    { label: "Aug 17", value: 3 },
    { label: "Aug 24", value: 5 },
    { label: "Aug 31", value: 8 },
    { label: "Sep 7", value: 2 },
  ];

  it("defaults to month view and displays monthly pull requests", () => {
    render(
      <TendrilDashboard
        id="dash"
        eventHandler={vi.fn()}
        pullRequests={monthlyPrs}
        pullRequestsWeekly={weeklyPrs}
      />,
    );

    const monthBtn = screen.getByRole("button", { name: "Month" });
    const weekBtn = screen.getByRole("button", { name: "Week" });

    expect(monthBtn).toHaveAttribute("data-active", "true");
    expect(weekBtn).toHaveAttribute("data-active", "false");

    expect(screen.getByText("Apr")).toBeInTheDocument();
    expect(screen.getByText("Sep")).toBeInTheDocument();
    expect(screen.queryByText("Aug 24")).not.toBeInTheDocument();
  });

  it("switches to week view on click and restores month view when clicked again", () => {
    render(
      <TendrilDashboard
        id="dash"
        eventHandler={vi.fn()}
        pullRequests={monthlyPrs}
        pullRequestsWeekly={weeklyPrs}
      />,
    );

    const monthBtn = screen.getByRole("button", { name: "Month" });
    const weekBtn = screen.getByRole("button", { name: "Week" });

    // Switch to Week
    fireEvent.click(weekBtn);
    expect(weekBtn).toHaveAttribute("data-active", "true");
    expect(monthBtn).toHaveAttribute("data-active", "false");

    expect(screen.getByText("Aug 24")).toBeInTheDocument();
    expect(screen.getByText("Sep 7")).toBeInTheDocument();
    expect(screen.queryByText("Apr")).not.toBeInTheDocument();

    // Switch back to Month
    fireEvent.click(monthBtn);
    expect(monthBtn).toHaveAttribute("data-active", "true");
    expect(weekBtn).toHaveAttribute("data-active", "false");

    expect(screen.getByText("Apr")).toBeInTheDocument();
    expect(screen.queryByText("Aug 24")).not.toBeInTheDocument();
  });

  it("renders weekly labels with formatted two-line date wrapping while remaining accessible", () => {
    render(
      <TendrilDashboard
        id="dash"
        eventHandler={vi.fn()}
        pullRequests={monthlyPrs}
        pullRequestsWeekly={weeklyPrs}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Week" }));

    const labelAug3 = screen.getByText("Aug 3");
    const labelAug10 = screen.getByText("Aug 10");
    const labelSep7 = screen.getByText("Sep 7");

    expect(labelAug3).toBeInTheDocument();
    expect(labelAug10).toBeInTheDocument();
    expect(labelSep7).toBeInTheDocument();

    expect(labelAug3.textContent).toBe("Aug\n3");
    expect(labelAug10.textContent).toBe("Aug\n10");
    expect(labelSep7.textContent).toBe("Sep\n7");
  });
});

