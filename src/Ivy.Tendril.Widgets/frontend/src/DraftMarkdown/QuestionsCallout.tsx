import React from "react";

const HelpCircleIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <circle cx="12" cy="12" r="10" />
    <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3" />
    <path d="M12 17h.01" />
  </svg>
);

export const QuestionsCallout: React.FC<{ content: string }> = ({ content }) => (
  <div className="pmv-questions" role="note">
    <div className="pmv-questions-header">
      <HelpCircleIcon />
      <span className="pmv-questions-title">Questions</span>
    </div>
    <div className="pmv-questions-content">{content}</div>
  </div>
);
