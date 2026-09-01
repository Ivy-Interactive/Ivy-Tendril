import React, { useEffect, useRef } from "react";

interface SearchOverlayProps {
  query: string;
  onQueryChange: (query: string) => void;
  matchCount: number;
  currentIndex: number;
  onNext: () => void;
  onPrevious: () => void;
  onClose: () => void;
}

export const SearchOverlay: React.FC<SearchOverlayProps> = ({
  query,
  onQueryChange,
  matchCount,
  currentIndex,
  onNext,
  onPrevious,
  onClose,
}) => {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
    inputRef.current?.select();
  }, []);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      if (e.shiftKey) {
        onPrevious();
      } else {
        onNext();
      }
    } else if (e.key === "Escape") {
      e.preventDefault();
      onClose();
    }
  };

  const counterText = query.trim()
    ? matchCount === 0
      ? "No matches"
      : `${currentIndex + 1} of ${matchCount}`
    : "";

  return (
    <div className="pmv-search-overlay" role="search">
      <input
        ref={inputRef}
        type="text"
        className="pmv-search-input"
        placeholder="Find in document..."
        aria-label="Find in document"
        value={query}
        onChange={(e) => onQueryChange(e.target.value)}
        onKeyDown={handleKeyDown}
      />
      {counterText && <span className="pmv-search-count">{counterText}</span>}
      <button
        type="button"
        className="pmv-search-btn"
        onClick={onPrevious}
        disabled={matchCount === 0}
        aria-label="Previous match"
        title="Previous match (Shift+Enter)"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="18 15 12 9 6 15" />
        </svg>
      </button>
      <button
        type="button"
        className="pmv-search-btn"
        onClick={onNext}
        disabled={matchCount === 0}
        aria-label="Next match"
        title="Next match (Enter)"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="6 9 12 15 18 9" />
        </svg>
      </button>
      <button
        type="button"
        className="pmv-search-btn pmv-search-btn--close"
        onClick={onClose}
        aria-label="Close search"
        title="Close (Escape)"
      >
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <line x1="18" y1="6" x2="6" y2="18" />
          <line x1="6" y1="6" x2="18" y2="18" />
        </svg>
      </button>
    </div>
  );
};
