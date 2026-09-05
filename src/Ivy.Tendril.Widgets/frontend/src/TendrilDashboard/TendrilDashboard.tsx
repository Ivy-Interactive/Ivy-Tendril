import React, { useState } from "react";
import {
  Check,
  Eye,
  Feather,
  LoaderCircle,
  MessageSquareWarning,
  Sprout,
  TrendingDown,
  TrendingUp,
} from "lucide-react";
import {
  TendrilDashboardProps,
  formatCountTick,
  formatCurrencyTick,
  hasSlotContent,
} from "./types";
import { TrendChart } from "./TrendChart";
import { ActivityGrid } from "./ActivityGrid";
import { PillBars } from "./PillBars";
import "./dashboard.css";

interface StatusItemProps {
  icon: React.ReactNode;
  count: number;
  label: string;
  onClick: () => void;
}

const StatusItem: React.FC<StatusItemProps> = ({ icon, count, label, onClick }) => (
  <button type="button" className="tdb-status-item" onClick={onClick}>
    {icon}
    <span className="tdb-status-count">{count}</span>
    <span className="tdb-status-label">{label}</span>
  </button>
);

const formatCurrencyValue = (value: number): string =>
  value >= 1000
    ? `$${Math.round(value).toLocaleString("en-US")}`
    : `$${value.toFixed(2)}`;

const formatPlansValue = (value: number): string =>
  `${Math.round(value)} plan${Math.round(value) === 1 ? "" : "s"}`;

export const TendrilDashboard: React.FC<TendrilDashboardProps> = ({
  id,
  events = [],
  eventHandler,
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
  trendWeekly = null,
  pullRequests = [],
  activity = [],
  jobs = [],
  slots,
}) => {
  const [tab, setTab] = useState<"cost" | "plans">("cost");
  const [sideTab, setSideTab] = useState<"git" | "prs">("git");
  const [trendPeriod, setTrendPeriod] = useState<"month" | "week">("month");

  const fireEvent = (eventName: string) => {
    if (events.includes(eventName)) {
      eventHandler(eventName, id, []);
    }
  };

  const fireJobEvent = (jobId: string) => {
    if (events.includes("OnJob")) {
      eventHandler("OnJob", id, [jobId]);
    }
  };

  const statusItems = [
    { icon: <Feather size={16} />, count: draftCount, label: "Plans", event: "OnDrafts" },
    { icon: <Sprout size={16} />, count: inProgressCount, label: "In Progress", event: "OnJobs" },
    { icon: <Eye size={16} />, count: reviewCount, label: "Ready For Review", event: "OnReview" },
    { icon: <Check size={16} />, count: completedCount, label: "Completed", event: "OnJobs" },
    { icon: <MessageSquareWarning size={16} />, count: failedCount, label: "Failed", event: "OnJobs" },
  ];

  const activeTrend = trendPeriod === "week" && trendWeekly ? trendWeekly : trend;
  const currentTrendName = trendPeriod === "week" ? "Last 4 weeks" : "Last 12 months";
  const previousTrendName = trendPeriod === "week" ? "Previous 4 weeks" : "Previous year";

  const trendData =
    activeTrend == null
      ? null
      : tab === "cost"
        ? {
            values: activeTrend.cost,
            previous: activeTrend.prevCost,
            formatTick: formatCurrencyTick,
            formatValue: formatCurrencyValue,
          }
        : {
            values: activeTrend.plans,
            previous: activeTrend.prevPlans,
            formatTick: formatCountTick,
            formatValue: formatPlansValue,
          };

  return (
    <div className="tdb-root remove-parent-padding">
      <div className="tdb-inner">
        <div className="tdb-grid">
          <div className="tdb-col">
            <header className="tdb-header">
              <div className="tdb-date">{dateText}</div>
              <h1 className="tdb-greeting">{greeting}</h1>
              <h1 className="tdb-headline">{headline}</h1>
            </header>

            <div className="tdb-block tdb-status">
              {statusItems.map((item, index) => (
                <React.Fragment key={item.label}>
                  {index > 0 && <div className="tdb-status-sep" />}
                  <StatusItem
                    icon={item.icon}
                    count={item.count}
                    label={item.label}
                    onClick={() => fireEvent(item.event)}
                  />
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
                    {kpi.hint && <div className="tdb-kpi-hint">{kpi.hint}</div>}
                  </div>
                ))}
              </div>
            )}

            {trendData && (
              <div className="tdb-block tdb-trend">
                <div className="tdb-trend-header">
                  <div className="tdb-tabs">
                    <button
                      type="button"
                      className="tdb-tab"
                      data-active={tab === "cost"}
                      onClick={() => setTab("cost")}
                    >
                      Total Cost
                    </button>
                    <button
                      type="button"
                      className="tdb-tab"
                      data-active={tab === "plans"}
                      onClick={() => setTab("plans")}
                    >
                      Total Plans
                    </button>
                  </div>
                  {trendWeekly && (
                    <>
                      <div className="tdb-trend-sep" />
                      <div className="tdb-granularity-toggle">
                        <button
                          type="button"
                          className="tdb-granularity-btn"
                          data-active={trendPeriod === "month"}
                          onClick={() => setTrendPeriod("month")}
                        >
                          Month
                        </button>
                        <button
                          type="button"
                          className="tdb-granularity-btn"
                          data-active={trendPeriod === "week"}
                          onClick={() => setTrendPeriod("week")}
                        >
                          Week
                        </button>
                      </div>
                    </>
                  )}
                  <div className="tdb-trend-sep" />
                  <div className="tdb-legend">
                    <span className="tdb-legend-item">
                      <span className="tdb-legend-dot" />
                      {currentTrendName}
                    </span>
                    <span className="tdb-legend-item">
                      <span className="tdb-legend-dash" />
                      {previousTrendName}
                    </span>
                  </div>
                </div>
                <div className="tdb-trend-chart">
                  <TrendChart
                    labels={activeTrend!.months}
                    values={trendData.values}
                    previous={trendData.previous}
                    currentName={currentTrendName}
                    previousName={previousTrendName}
                    formatTick={trendData.formatTick}
                    formatValue={trendData.formatValue}
                  />
                </div>
              </div>
            )}

          </div>

          <div className="tdb-col tdb-col-side">
            <div className="tdb-update-slot">{slots?.UpdateNotice}</div>
            {hasSlotContent(slots?.TunnelQr) ? (
              <>
                {/* With the tunnel card present, Git Activity and Pull Requests
                    merge into one tabbed card so the column keeps the same
                    number of rows as without a tunnel. */}
                <div className="tdb-block tdb-side-block">
                  <div className="tdb-side-head">
                    <div className="tdb-tabs">
                      <button
                        type="button"
                        className="tdb-tab"
                        data-active={sideTab === "git"}
                        onClick={() => setSideTab("git")}
                      >
                        Git Activity
                      </button>
                      <button
                        type="button"
                        className="tdb-tab"
                        data-active={sideTab === "prs"}
                        onClick={() => setSideTab("prs")}
                      >
                        Pull Requests
                      </button>
                    </div>
                  </div>
                  <div className="tdb-side-body">
                    {sideTab === "git" ? (
                      <ActivityGrid months={activity} />
                    ) : (
                      <PillBars items={pullRequests} />
                    )}
                  </div>
                </div>
                <div className="tdb-block tdb-side-block tdb-tunnel">
                  <div className="tdb-side-head">
                    <div className="tdb-block-title">Tunnel</div>
                    {slots?.TunnelMenu}
                  </div>
                  <div className="tdb-tunnel-body">{slots?.TunnelQr}</div>
                </div>
              </>
            ) : (
              <>
                <div className="tdb-block tdb-side-block">
                  <div className="tdb-side-head">
                    <div className="tdb-block-title">Git Activity</div>
                  </div>
                  <div className="tdb-side-body">
                    <ActivityGrid months={activity} />
                  </div>
                </div>
                <div className="tdb-block tdb-side-block">
                  <div className="tdb-side-head">
                    <div className="tdb-block-title">Pull Requests</div>
                  </div>
                  <div className="tdb-side-body">
                    <PillBars items={pullRequests} />
                  </div>
                </div>
              </>
            )}
          </div>

          {/* The factory and jobs cards share the grid's second row so their
              tops and bottoms always align across the two columns. */}
          <div className="tdb-block tdb-factory">
            <div className="tdb-block-title">Software Factory</div>
            <div className="tdb-factory-body">{slots?.ProcessViewer}</div>
          </div>

          <div className="tdb-block tdb-side-block tdb-jobs">
            <div className="tdb-block-title">Active Jobs</div>
            <div className="tdb-jobs-list">
              {jobs.length === 0 && (
                <div className="tdb-empty-note">No jobs running</div>
              )}
              {jobs.map((job) => (
                <button
                  key={job.id}
                  type="button"
                  className="tdb-job-row"
                  onClick={() => fireJobEvent(job.id)}
                >
                  <LoaderCircle
                    size={14}
                    className="tdb-job-spinner"
                    data-spinning={job.status === "running"}
                  />
                  {job.planId && <span className="tdb-job-tag">{job.planId}</span>}
                  <span className="tdb-job-title">{job.title}</span>
                </button>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
