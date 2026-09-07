import React, { useLayoutEffect, useMemo, useRef } from "react";
import {
  computeActivityMetrics,
  DashboardActivityMonthDto,
  rampLevel,
} from "./types";
import { HoverTip, useHoverTip } from "./HoverTip";

interface ActivityGridProps {
  months: DashboardActivityMonthDto[];
}

interface DayCell {
  date: string;
  count: number;
  dateObj: Date;
}

interface WeekColumn {
  monthLabel?: string;
  days: (DayCell | null)[];
}

const WEEKDAYS = ["Mon", "", "Wed", "", "Fri", "", ""];

/** GitHub-style contribution heatmap with 7 weekday rows, week columns,
    month headers, and an intensity ramp legend. */
export const ActivityGrid: React.FC<ActivityGridProps> = ({ months }) => {
  const { wrapRef, tip, showTip, hideTip } = useHoverTip();
  const scrollRef = useRef<HTMLDivElement>(null);

  const { totalPrs, activeWeeks } = useMemo(
    () => computeActivityMetrics(months),
    [months],
  );

  // Extract all daily records across the months
  const allDays = useMemo(() => {
    const days = months.flatMap((m) => m.days ?? []);
    if (days.length > 0) return days;

    // Fallback if days were not supplied: synthesize days from months & weeks
    const fallback: { date: string; count: number }[] = [];
    const today = new Date();
    for (let mi = 0; mi < months.length; mi++) {
      const m = months[mi];
      const monthOffset = months.length - 1 - mi;
      const d = new Date(today.getFullYear(), today.getMonth() - monthOffset, 1);
      const daysInMonth = new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
      for (let day = 1; day <= daysInMonth; day++) {
        const curDate = new Date(d.getFullYear(), d.getMonth(), day);
        const curIso = curDate.toISOString().slice(0, 10);
        const weekIdx = Math.min(m.weeks.length - 1, Math.floor((day - 1) / 7));
        const count = m.weeks[weekIdx] ?? 0;
        fallback.push({ date: curIso, count });
      }
    }
    return fallback;
  }, [months]);

  // Max daily count for the color ramp
  const maxDay = useMemo(() => {
    if (allDays.length === 0) return 0;
    return Math.max(0, ...allDays.map((d) => d.count));
  }, [allDays]);

  // Build weekly columns (Monday=0 to Sunday=6)
  const columns = useMemo<WeekColumn[]>(() => {
    if (allDays.length === 0) return [];

    const byDate = new Map<string, number>();
    for (const d of allDays) {
      byDate.set(d.date, d.count);
    }

    const firstDateStr = allDays[0].date;
    const lastDateStr = allDays[allDays.length - 1].date;

    const firstDate = new Date(firstDateStr + "T00:00:00");
    const lastDate = new Date(lastDateStr + "T00:00:00");

    // Align start to the Monday of the first week
    const startMonday = new Date(firstDate);
    const firstDayOfWeek = (firstDate.getDay() + 6) % 7;
    startMonday.setDate(startMonday.getDate() - firstDayOfWeek);

    const cols: WeekColumn[] = [];
    const cur = new Date(startMonday);
    let lastMonth = -1;

    while (cur <= lastDate || (cur.getDay() + 6) % 7 !== 0) {
      const weekDays: (DayCell | null)[] = [];
      let monthLabelForWeek: string | undefined = undefined;

      for (let dayIdx = 0; dayIdx < 7; dayIdx++) {
        const curDateStr = cur.toISOString().slice(0, 10);
        if (cur < firstDate || cur > lastDate) {
          weekDays.push(null);
        } else {
          const count = byDate.get(curDateStr) ?? 0;
          weekDays.push({
            date: curDateStr,
            count,
            dateObj: new Date(cur),
          });

          // Label month when month changes
          const monthIdx = cur.getMonth();
          if (monthIdx !== lastMonth && monthLabelForWeek === undefined) {
            monthLabelForWeek = cur.toLocaleDateString("en-US", { month: "short" });
            lastMonth = monthIdx;
          }
        }
        cur.setDate(cur.getDate() + 1);
      }

      cols.push({
        monthLabel: monthLabelForWeek,
        days: weekDays,
      });
    }

    return cols;
  }, [allDays]);

  // Auto-scroll to the end (most recent activity) on mount
  useLayoutEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollLeft = scrollRef.current.scrollWidth;
    }
  }, [columns]);

  if (months.length === 0) {
    return (
      <div className="tdb-empty-note">
        No activity yet
      </div>
    );
  }

  return (
    <div className="tdb-tip-wrap" ref={wrapRef}>
      <div className="tdb-activity-wrap">
        <div className="tdb-activity-scroll" ref={scrollRef}>
          <div className="tdb-activity-layout">
            <div className="tdb-activity-weekdays">
              {WEEKDAYS.map((wd, i) => (
                <div key={i} className="tdb-activity-weekday">
                  {wd}
                </div>
              ))}
            </div>
            <div className="tdb-activity-grid-body">
              <div className="tdb-activity-months-row">
                {columns.map((col, idx) => (
                  <div key={idx} className="tdb-activity-month-col">
                    {col.monthLabel && (
                      <span className="tdb-activity-month-label">
                        {col.monthLabel}
                      </span>
                    )}
                  </div>
                ))}
              </div>
              <div className="tdb-activity-weeks">
                {columns.map((col, colIdx) => (
                  <div key={colIdx} className="tdb-activity-week">
                    {col.days.map((day, dayIdx) => {
                      if (!day) {
                        return (
                          <div
                            key={dayIdx}
                            className="tdb-activity-cell tdb-cell-empty"
                          />
                        );
                      }
                      const dateText = day.dateObj.toLocaleDateString("en-US", {
                        weekday: "short",
                        month: "short",
                        day: "numeric",
                        year: "numeric",
                      });
                      const countText =
                        day.count === 0
                          ? "No pull requests merged"
                          : `${day.count} pull request${day.count === 1 ? "" : "s"} merged`;

                      return (
                        <div
                          key={dayIdx}
                          className="tdb-activity-cell"
                          data-level={rampLevel(day.count, maxDay)}
                          onMouseEnter={showTip(dateText, countText)}
                          onMouseLeave={hideTip}
                        />
                      );
                    })}
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
        <div className="tdb-activity-footer">
          <div className="tdb-activity-legend">
            <span>Less</span>
            <div className="tdb-activity-cell" data-level={0} />
            <div className="tdb-activity-cell" data-level={1} />
            <div className="tdb-activity-cell" data-level={2} />
            <div className="tdb-activity-cell" data-level={3} />
            <div className="tdb-activity-cell" data-level={4} />
            <span>More</span>
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
