import React, { useMemo } from "react";
import { DashboardActivityMonthDto, rampLevel } from "./types";
import { HoverTip, useHoverTip } from "./HoverTip";

interface ActivityGridProps {
  months: DashboardActivityMonthDto[];
}

/** One column per month; each week with activity renders as a cell stacked
    from the bottom, shaded by that week's intensity relative to the range. */
export const ActivityGrid: React.FC<ActivityGridProps> = ({ months }) => {
  const { wrapRef, tip, showTip, hideTip } = useHoverTip();
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
      <div className="tdb-activity-scroll">
        <div className="tdb-activity">
          {months.map((month, monthIndex) => (
            <div className="tdb-activity-col" key={monthIndex}>
              {month.weeks.map((count, weekIndex) =>
                count > 0 ? (
                  <div
                    key={weekIndex}
                    className="tdb-activity-cell"
                    data-level={rampLevel(count, maxWeek)}
                    onMouseEnter={showTip(
                      `${month.label}, week ${weekIndex + 1}`,
                      `${count} PR${count === 1 ? "" : "s"} merged`,
                    )}
                    onMouseLeave={hideTip}
                  />
                ) : null,
              )}
            </div>
          ))}
        </div>
        <div className="tdb-activity-labels">
          {months.map((month, monthIndex) => (
            <div className="tdb-activity-label" key={monthIndex}>
              {(months.length - 1 - monthIndex) % step === 0 ? month.label : ""}
            </div>
          ))}
        </div>
      </div>
      <HoverTip tip={tip} />
    </div>
  );
};
