import React from "react";
import { Plus, Pencil, ThumbsUp, LoaderCircle } from "lucide-react";
import { TendrilProcessViewProps } from "./types";
import { getWidth, getHeight } from "./styles";
import "./TendrilProcessView.css";

interface ArrowProps {
  count: number;
  spinning: boolean;
  onClick: () => void;
}

const Arrow: React.FC<ArrowProps> = ({ count, spinning, onClick }) => (
  <div className="tpv-arrow-segment">
    <button className="tpv-arrow-label" onClick={onClick}>
      <span className="tpv-arrow-count">{count}</span>
      {spinning && <LoaderCircle className="tpv-spinner" size={13} />}
    </button>
    <svg className="tpv-arrow-svg" viewBox="0 0 80 12" preserveAspectRatio="none">
      <defs>
        <marker id="arrowhead" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto">
          <polygon points="0,0 6,3 0,6" fill="currentColor" />
        </marker>
      </defs>
      <line x1="0" y1="6" x2="72" y2="6" stroke="currentColor" strokeWidth="1.5" markerEnd="url(#arrowhead)" />
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
    <svg className="tpv-curve-svg" viewBox="0 0 100 32" fill="none">
      <defs>
        <marker id="arrowhead-curve" markerWidth="7" markerHeight="7" refX="3.5" refY="3.5" orient="auto">
          <polygon points="0,0 7,3.5 0,7" fill="currentColor" />
        </marker>
      </defs>
      <path
        d="M80 30 L80 12 Q80 4, 72 4 L28 4 Q20 4, 20 12 L20 30"
        stroke="currentColor"
        strokeWidth="1.5"
        fill="none"
        markerEnd="url(#arrowhead-curve)"
      />
    </svg>
  </div>
);

export const TendrilProcessView: React.FC<TendrilProcessViewProps> = ({
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
}) => {
  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  const fireEvent = (eventName: string) => {
    if (events.includes(eventName)) {
      eventHandler(eventName, id, []);
    }
  };

  return (
    <div className="tpv-container" style={style}>
      <div className="tpv-flow">
        <button className="tpv-box tpv-box-create" onClick={() => fireEvent("OnCreate")}>
          <span className="tpv-box-label">Create Plan</span>
          <Plus size={16} className="tpv-box-icon" />
        </button>

        <Arrow
          count={creatingPlansCount}
          spinning={creatingPlansCount > 0}
          onClick={() => fireEvent("OnJobs")}
        />

        {/* Drafts with executing loop */}
        <div className="tpv-stage-wrapper">
          {executingPlansCount > 0 && (
            <LoopArrow count={executingPlansCount} onClick={() => fireEvent("OnJobs")} />
          )}
          <button className="tpv-box tpv-box-stage" onClick={() => fireEvent("OnDrafts")}>
            <Pencil size={14} className="tpv-box-stage-icon" />
            <span className="tpv-box-label">Drafts {draftCount}</span>
          </button>
        </div>

        <Arrow
          count={updatingPlansCount}
          spinning={updatingPlansCount > 0}
          onClick={() => fireEvent("OnJobs")}
        />

        {/* Review with retrying loop */}
        <div className="tpv-stage-wrapper">
          {retryingPlansCount > 0 && (
            <LoopArrow count={retryingPlansCount} onClick={() => fireEvent("OnJobs")} />
          )}
          <button className="tpv-box tpv-box-stage" onClick={() => fireEvent("OnReview")}>
            <ThumbsUp size={14} className="tpv-box-stage-icon" />
            <span className="tpv-box-label">Review {reviewCount}</span>
          </button>
        </div>
      </div>
    </div>
  );
};
