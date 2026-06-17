import React from "react";
import { Plus, Feather, ThumbsUp, LoaderCircle } from "lucide-react";
import { TendrilProcessViewerProps } from "./types";
import { getWidth, getHeight } from "../styles";
import "./tendril-process.css";

interface ArrowProps {
  count: number;
  spinning: boolean;
  onClick: () => void;
}

const Arrow: React.FC<ArrowProps> = ({ count, spinning, onClick }) => (
  <div className="tpv-arrow-segment">
    {count > 0 && (
      <button className="tpv-arrow-label" onClick={onClick}>
        <span className="tpv-arrow-count">{count}</span>
        {spinning && <LoaderCircle className="tpv-spinner" size={13} />}
      </button>
    )}
    <svg className="tpv-arrow-svg" viewBox="0 0 80 12" preserveAspectRatio="xMidYMid meet">
      <defs>
        <marker id="arrowhead" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto">
          <polygon points="0,0 6,3 0,6" fill="currentColor" />
        </marker>
      </defs>
      <line x1="0" y1="6" x2="78" y2="6" stroke="currentColor" strokeWidth="1.5" markerEnd="url(#arrowhead)" />
    </svg>
  </div>
);

interface LoopArrowProps {
  count: number;
  onClick: () => void;
}

const LoopArrow: React.FC<LoopArrowProps> = ({ count, onClick }) => (
  <div className="tpv-loop-arrow">
    <button className="tpv-arrow-label" onClick={onClick}>
      <span className="tpv-arrow-count">{count}</span>
      <LoaderCircle className="tpv-spinner" size={13} />
    </button>
    <svg className="tpv-curve-svg" viewBox="0 0 100 30" fill="none">
      <defs>
        <marker id="arrowhead-curve" markerWidth="6" markerHeight="6" refX="3" refY="3" orient="auto">
          <polygon points="0,0 6,3 0,6" fill="currentColor" />
        </marker>
      </defs>
      <path
        d="M75 30 L75 10 Q75 4, 68 4 L32 4 Q25 4, 25 10 L25 25"
        stroke="currentColor"
        strokeWidth="1.5"
        fill="none"
        markerEnd="url(#arrowhead-curve)"
      />
    </svg>
  </div>
);

export const TendrilProcessViewer: React.FC<TendrilProcessViewerProps> = ({
  id,
  width = "full",
  height = "fit",
  events = [],
  eventHandler,
  draftCount = 0,
  reviewCount = 0,
  creatingPlansCount = 0,
  updatingPlansCount = 0,
  executingPlansCount = 0,
  retryingPlansCount = 0,
  creatingPrCount = 0,
}) => {
  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  const allZero = draftCount === 0 && reviewCount === 0 && creatingPlansCount === 0
    && updatingPlansCount === 0 && executingPlansCount === 0 && retryingPlansCount === 0
    && creatingPrCount === 0;

  const fireEvent = (eventName: string) => {
    if (events.includes(eventName)) {
      eventHandler(eventName, id, []);
    }
  };

  return (
    <div className="tpv-container" style={style}>
      <div className="tpv-flow">
        <button className={`tpv-box tpv-box-create${allZero ? " tpv-pulse" : ""}`} onClick={() => fireEvent("OnCreate")}>
          <span className="tpv-box-label">New Plan</span>
          <Plus size={16} className="tpv-box-icon" />
        </button>

        <Arrow
          count={creatingPlansCount}
          spinning={creatingPlansCount > 0}
          onClick={() => fireEvent("OnJobs")}
        />

        {/* Drafts with updating loop */}
        <div className="tpv-stage-wrapper">
          {updatingPlansCount > 0 && (
            <LoopArrow count={updatingPlansCount} onClick={() => fireEvent("OnJobs")} />
          )}
          <button className="tpv-box tpv-box-stage" onClick={() => fireEvent("OnDrafts")}>
            <Feather size={14} className="tpv-box-stage-icon" />
            <span className="tpv-box-label">Drafts{draftCount > 0 && <span className="tpv-box-count">{draftCount}</span>}</span>
          </button>
        </div>

        <Arrow
          count={executingPlansCount}
          spinning={executingPlansCount > 0}
          onClick={() => fireEvent("OnJobs")}
        />

        {/* Review with retrying loop */}
        <div className="tpv-stage-wrapper">
          {retryingPlansCount > 0 && (
            <LoopArrow count={retryingPlansCount} onClick={() => fireEvent("OnJobs")} />
          )}
          <button className="tpv-box tpv-box-stage" onClick={() => fireEvent("OnReview")}>
            <ThumbsUp size={14} className="tpv-box-stage-icon" />
            <span className="tpv-box-label">Review{reviewCount > 0 && <span className="tpv-box-count">{reviewCount}</span>}</span>
          </button>
          {creatingPrCount > 0 && (
            <button className="tpv-sub-label" onClick={() => fireEvent("OnJobs")}>
              <span>PR {creatingPrCount}</span>
              <LoaderCircle className="tpv-spinner" size={13} />
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
