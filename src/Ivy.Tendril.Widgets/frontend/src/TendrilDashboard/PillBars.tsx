import React, { useMemo } from "react";
import { DashboardMonthValueDto, niceTicks, rampLevel } from "./types";

interface PillBarsProps {
  items: DashboardMonthValueDto[];
}

/** Rounded pill bars shaded by value intensity, with a small y-axis. */
export const PillBars: React.FC<PillBarsProps> = ({ items }) => {
  const max = useMemo(() => Math.max(0, ...items.map((i) => i.value)), [items]);

  if (items.length === 0 || max === 0) {
    return <div className="tdb-empty-note">No merged pull requests yet</div>;
  }

  const ticks = niceTicks(max);
  const scaleMax = ticks[ticks.length - 1];

  return (
    <div className="tdb-bars">
      <div className="tdb-bars-y">
        {[...ticks].reverse().map((tick) => (
          <span key={tick}>{tick}</span>
        ))}
      </div>
      <div className="tdb-bars-plot">
        {items.map((item, index) => (
          <div className="tdb-bar-item" key={index}>
            <div className="tdb-bar-track" title={`${item.label}: ${item.value} PR${item.value === 1 ? "" : "s"}`}>
              <div
                className="tdb-bar"
                data-level={rampLevel(item.value, scaleMax)}
                style={{ height: `${(item.value / scaleMax) * 100}%` }}
              />
            </div>
            <span className="tdb-bar-label">{item.label}</span>
          </div>
        ))}
      </div>
    </div>
  );
};
