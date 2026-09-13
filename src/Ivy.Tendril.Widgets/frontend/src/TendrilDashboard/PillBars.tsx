import React, { useMemo } from "react";
import { DashboardMonthValueDto, niceTicks, rampLevel } from "./types";
import { HoverTip, useHoverTip } from "./HoverTip";

interface PillBarsProps {
  items: DashboardMonthValueDto[];
}

export function getAccessibleBarLabel(item: DashboardMonthValueDto): string {
  const prText = `${item.value} pull request${item.value === 1 ? "" : "s"} merged`;
  let year = item.year;
  let month = item.month;
  let day = item.day;

  if ((!year || !month) && item.date) {
    const parts = item.date.split("-").map(Number);
    if (parts.length >= 2 && !isNaN(parts[0]) && !isNaN(parts[1])) {
      year = parts[0];
      month = parts[1];
      if (parts.length >= 3 && !isNaN(parts[2])) {
        day = parts[2];
      }
    }
  }

  if (year && month) {
    const date = new Date(Date.UTC(year, month - 1, day ?? 1));
    const monthName = date.toLocaleDateString("en-US", { month: "long", timeZone: "UTC" });
    const isWeekly = item.label.includes(" ") || /\d/.test(item.label) || (day !== undefined && day > 1);
    if (isWeekly && day !== undefined) {
      return `Week of ${monthName} ${day}, ${year}: ${prText}`;
    }
    return `${monthName} ${year}: ${prText}`;
  }

  return `${item.label}: ${prText}`;
}

export function getBarTooltipHeader(item: DashboardMonthValueDto): string {
  let year = item.year;
  let month = item.month;
  let day = item.day;

  if ((!year || !month) && item.date) {
    const parts = item.date.split("-").map(Number);
    if (parts.length >= 2 && !isNaN(parts[0]) && !isNaN(parts[1])) {
      year = parts[0];
      month = parts[1];
      if (parts.length >= 3 && !isNaN(parts[2])) {
        day = parts[2];
      }
    }
  }

  if (year && month) {
    const date = new Date(Date.UTC(year, month - 1, day ?? 1));
    const monthName = date.toLocaleDateString("en-US", { month: "long", timeZone: "UTC" });
    const isWeekly = item.label.includes(" ") || /\d/.test(item.label) || (day !== undefined && day > 1);
    if (isWeekly && day !== undefined) {
      return `Week of ${monthName} ${day}, ${year}`;
    }
    return `${monthName} ${year}`;
  }

  return item.label;
}

/** Rounded pill bars shaded by value intensity, with a small y-axis. */
export const PillBars: React.FC<PillBarsProps> = ({ items }) => {
  const { wrapRef, tip, showTip, hideTip } = useHoverTip();
  const max = useMemo(() => Math.max(0, ...items.map((i) => i.value)), [items]);

  if (items.length === 0 || max === 0) {
    return (
      <div className="tdb-empty-note">
        No merged pull requests yet
      </div>
    );
  }

  const ticks = niceTicks(max);
  const scaleMax = ticks[ticks.length - 1];

  return (
    <div className="tdb-tip-wrap" ref={wrapRef}>
      <div className="tdb-bars">
        <div className="tdb-bars-y">
          {[...ticks].reverse().map((tick) => (
            <span key={tick}>{tick}</span>
          ))}
        </div>
        <div className="tdb-bars-plot" role="list">
          {items.map((item, index) => {
            const accessibleLabel = getAccessibleBarLabel(item);
            return (
              <div className="tdb-bar-item" key={index} role="listitem">
                <div className="tdb-bar-track">
                  <div
                    className="tdb-bar"
                    role="img"
                    aria-label={accessibleLabel}
                    data-level={rampLevel(item.value, scaleMax)}
                    style={{ height: `${(item.value / scaleMax) * 100}%` }}
                    onMouseEnter={showTip(
                      getBarTooltipHeader(item),
                      `${item.value} PR${item.value === 1 ? "" : "s"} merged`,
                    )}
                    onMouseLeave={hideTip}
                  />
                </div>
                <span className="tdb-bar-label">{item.label.replace(" ", "\n")}</span>
              </div>
            );
          })}
        </div>
      </div>
      <HoverTip tip={tip} />
    </div>
  );
};
