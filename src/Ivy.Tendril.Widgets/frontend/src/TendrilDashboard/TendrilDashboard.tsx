import React, { useState } from "react";
import {
  Check,
  Eye,
  Feather,
  MessageSquareWarning,
  Sprout,
  TrendingDown,
  TrendingUp,
} from "lucide-react";
import {
  TendrilDashboardProps,
  formatCountTick,
  formatCurrencyTick,
} from "./types";
import { TrendChart } from "./TrendChart";
import { ActivityGrid } from "./ActivityGrid";
import { PillBars } from "./PillBars";
import "./dashboard.css";

interface StatusItemProps {
  icon: React.ReactNode;
  count: number;
  label: string;
}

const StatusItem: React.FC<StatusItemProps> = ({ icon, count, label }) => (
  <div className="tdb-status-item">
    {icon}
    <span className="tdb-status-count">{count}</span>
    <span className="tdb-status-label">{label}</span>
  </div>
);

const formatCurrencyValue = (value: number): string =>
  value >= 1000
    ? `$${Math.round(value).toLocaleString("en-US")}`
    : `$${value.toFixed(2)}`;

const formatPlansValue = (value: number): string => `${Math.round(value)}`;

export const TendrilDashboard: React.FC<TendrilDashboardProps> = ({
  dateText = "",
  greeting = "",
  headline = "",
  draftCount = 0,
  inProgressCount = 0,
  reviewCount = 0,
  completedCount = 0,
  failedCount = 0,
  kpis = [],
  trend = null,
  pullRequests = [],
  activity = [],
  slots,
}) => {
  const [tab, setTab] = useState<"cost" | "plans">("cost");

  const statusItems = [
    { icon: <Feather size={16} />, count: draftCount, label: "Drafts" },
    { icon: <Sprout size={16} />, count: inProgressCount, label: "In Progress" },
    { icon: <Eye size={16} />, count: reviewCount, label: "Ready For Review" },
    { icon: <Check size={16} />, count: completedCount, label: "Completed" },
    { icon: <MessageSquareWarning size={16} />, count: failedCount, label: "Failed" },
  ];

  const trendData =
    trend == null
      ? null
      : tab === "cost"
        ? {
            current: trend.costThisYear,
            previous: trend.costLastYear,
            formatTick: formatCurrencyTick,
            formatValue: formatCurrencyValue,
          }
        : {
            current: trend.plansThisYear,
            previous: trend.plansLastYear,
            formatTick: formatCountTick,
            formatValue: formatPlansValue,
          };

  return (
    <div className="tdb-root remove-parent-padding">
      <div className="tdb-inner">
        <header className="tdb-header">
          <div className="tdb-date">{dateText}</div>
          <h1 className="tdb-greeting">{greeting}</h1>
          <h1 className="tdb-headline">{headline}</h1>
        </header>

        <div className="tdb-grid">
          <div className="tdb-col">
            <div className="tdb-block tdb-status">
              {statusItems.map((item, index) => (
                <React.Fragment key={item.label}>
                  {index > 0 && <div className="tdb-status-sep" />}
                  <StatusItem {...item} />
                </React.Fragment>
              ))}
            </div>

            {kpis.length > 0 && (
              <div className="tdb-kpis">
                {kpis.map((kpi, index) => (
                  <div className="tdb-kpi" data-tone={index % 4} key={kpi.label}>
                    <div className="tdb-kpi-label">{kpi.label}</div>
                    <div className="tdb-kpi-row">
                      <span className="tdb-kpi-value">{kpi.value}</span>
                      {kpi.delta && (
                        <span className="tdb-kpi-delta">
                          {kpi.delta}
                          {kpi.direction === "down" ? (
                            <TrendingDown />
                          ) : (
                            <TrendingUp />
                          )}
                        </span>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}

            {trendData && (
              <div className="tdb-block tdb-trend">
                <div className="tdb-trend-header">
                  <div className="tdb-tabs">
                    <button
                      className="tdb-tab"
                      data-active={tab === "cost"}
                      onClick={() => setTab("cost")}
                    >
                      Total Cost
                    </button>
                    <button
                      className="tdb-tab"
                      data-active={tab === "plans"}
                      onClick={() => setTab("plans")}
                    >
                      Total Plans
                    </button>
                  </div>
                  <div className="tdb-trend-sep" />
                  <div className="tdb-legend">
                    <span className="tdb-legend-item">
                      <span className="tdb-legend-dot" />
                      This year
                    </span>
                    <span className="tdb-legend-item">
                      <span className="tdb-legend-dash" />
                      Last year
                    </span>
                  </div>
                </div>
                <div className="tdb-trend-chart">
                  <TrendChart
                    labels={trend!.months}
                    current={trendData.current}
                    previous={trendData.previous}
                    formatTick={trendData.formatTick}
                    formatValue={trendData.formatValue}
                    currentName="This year"
                    previousName="Last year"
                  />
                </div>
              </div>
            )}

            <div className="tdb-block tdb-factory">
              <div className="tdb-block-title">Software Factory</div>
              <div className="tdb-factory-body">{slots?.ProcessViewer}</div>
            </div>
          </div>

          <div className="tdb-col tdb-col-side">
            <div className="tdb-block tdb-side-block">
              <div className="tdb-block-title">Git Activity</div>
              <div className="tdb-side-body">
                <ActivityGrid months={activity} />
              </div>
            </div>
            <div className="tdb-block tdb-side-block">
              <div className="tdb-block-title">Pull Requests</div>
              <div className="tdb-side-body">
                <PillBars items={pullRequests} />
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
