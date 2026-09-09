import React, { useMemo } from "react";
import {
  computeActivityMetrics,
  DashboardActivityMonthDto,
  rampLevel,
} from "./types";
import { HoverTip, useHoverTip } from "./HoverTip";

interface ActivityGridProps {
  months: DashboardActivityMonthDto[];
}

const parseIsoDate = (iso: string): Date => {
  const [year, month, day] = iso.split("-").map(Number);
  return new Date(year, month - 1, day);
};

/**
 * Monday of the calendar week for each week index in a month, derived from its daily breakdown
 * (grouped the same way the backend groups `days` into `weeks`, see `BuildActivityMonths`).
 * Returns an empty map when `days` isn't supplied — e.g. by the samples app — since the `weeks`
 * array alone has no date information to recover.
 */
const weekStartDates = (month: DashboardActivityMonthDto): Map<number, Date> => {
  const starts = new Map<number, Date>();
  if (!month.days || month.days.length === 0) return starts;

  const firstDate = parseIsoDate(month.days[0].date);
  const offset = (firstDate.getDay() + 6) % 7;

  month.days.forEach((day, index) => {
    const weekIndex = Math.floor((offset + index) / 7);
    if (starts.has(weekIndex)) return;
    const date = parseIsoDate(day.date);
    const dayOfWeek = (date.getDay() + 6) % 7;
    date.setDate(date.getDate() - dayOfWeek);
    starts.set(weekIndex, date);
  });

  return starts;
};

/** One column per month; each week with activity renders as a cell stacked
    from the bottom, shaded by that week's intensity relative to the range. */
export const ActivityGrid: React.FC<ActivityGridProps> = ({ months }) => {
  const { wrapRef, tip, showTip, hideTip } = useHoverTip();

  const { totalPrs, activeWeeks } = useMemo(
    () => computeActivityMetrics(months),
    [months],
  );

  const maxWeek = useMemo(
    () => Math.max(0, ...months.flatMap((m) => m.weeks)),
    [months],
  );

  if (months.length === 0 || maxWeek === 0) {
    return <div className="tdb-empty-note">No merged pull requests yet</div>;
  }

  // Label every other month when the range is long, always including the last.
  const step = months.length > 8 ? 2 : 1;

  return (
    <div className="tdb-tip-wrap" ref={wrapRef}>
      <div className="tdb-activity-wrap">
        <div className="tdb-activity-scroll">
          <div className="tdb-activity">
            {months.map((month, monthIndex) => {
              const starts = weekStartDates(month);
              return (
                <div className="tdb-activity-col" key={monthIndex}>
                  {month.weeks
                    .map((count, weekIndex) => ({ count, weekIndex }))
                    .filter(({ count }) => count > 0)
                    .map(({ count, weekIndex }) => {
                      const weekStart = starts.get(weekIndex);
                      const title = weekStart
                        ? `Week of ${weekStart.toLocaleDateString("en-US", {
                            weekday: "short",
                            month: "short",
                            day: "numeric",
                          })}`
                        : month.label;
                      const body = `${count} pull request${count === 1 ? "" : "s"} merged`;
                      return (
                        <div
                          key={weekIndex}
                          className="tdb-activity-cell"
                          data-level={rampLevel(count, maxWeek)}
                          onMouseEnter={showTip(title, body)}
                          onMouseLeave={hideTip}
                        />
                      );
                    })}
                </div>
              );
            })}
          </div>
          <div className="tdb-activity-labels">
            {months.map((month, monthIndex) => (
              <div className="tdb-activity-label" key={monthIndex}>
                {(months.length - 1 - monthIndex) % step === 0 ? month.label : ""}
              </div>
            ))}
          </div>
        </div>
        <div className="tdb-activity-metrics">
          <div className="tdb-activity-metric">
            <span className="tdb-activity-metric-value">{totalPrs}</span>
            <span className="tdb-activity-metric-label">PRs merged</span>
          </div>
          <div className="tdb-activity-metric">
            <span className="tdb-activity-metric-value">{activeWeeks}</span>
            <span className="tdb-activity-metric-label">Active weeks</span>
          </div>
        </div>
      </div>
      <HoverTip tip={tip} />
    </div>
  );
};
