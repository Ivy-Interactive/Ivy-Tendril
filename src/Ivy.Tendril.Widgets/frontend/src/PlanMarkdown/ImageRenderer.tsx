import React, { useState, useCallback, useEffect } from "react";
import { createPortal } from "react-dom";

interface ImageRendererProps extends React.ImgHTMLAttributes<HTMLImageElement> {
  src?: string;
  alt?: string;
  title?: string;
}

const BrokenImageIcon = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
    <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
    <circle cx="8.5" cy="8.5" r="1.5" />
    <polyline points="21 15 16 10 5 21" />
    <line x1="3" y1="3" x2="21" y2="21" />
  </svg>
);

export const ImageRenderer: React.FC<ImageRendererProps> = ({ src, alt, title, ...props }) => {
  const [loadState, setLoadState] = useState<"loading" | "loaded" | "error">("loading");
  const [isOverlayOpen, setIsOverlayOpen] = useState(false);

  const handleLoad = useCallback(() => {
    setLoadState("loaded");
  }, []);

  const handleError = useCallback(() => {
    setLoadState("error");
  }, []);

  const handleImageClick = useCallback(() => {
    if (loadState === "loaded") {
      setIsOverlayOpen(true);
    }
  }, [loadState]);

  const handleCloseOverlay = useCallback(() => {
    setIsOverlayOpen(false);
  }, []);

  const handleBackdropClick = useCallback((e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      setIsOverlayOpen(false);
    }
  }, []);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape" && isOverlayOpen) {
        setIsOverlayOpen(false);
      }
    };

    if (isOverlayOpen) {
      document.addEventListener("keydown", handleEscape);
      document.body.style.overflow = "hidden";
    } else {
      document.body.style.overflow = "";
    }

    return () => {
      document.removeEventListener("keydown", handleEscape);
      document.body.style.overflow = "";
    };
  }, [isOverlayOpen]);

  if (loadState === "error") {
    return (
      <div className="pmv-img-error">
        <BrokenImageIcon />
        <div className="pmv-img-error-text">{alt || "Image failed to load"}</div>
        {title && <div className="pmv-img-error-title">{title}</div>}
      </div>
    );
  }

  return (
    <>
      <img
        {...props}
        src={src}
        alt={alt}
        title={title}
        className={loadState === "loaded" ? "pmv-img-clickable" : ""}
        onLoad={handleLoad}
        onError={handleError}
        onClick={handleImageClick}
      />
      {isOverlayOpen &&
        createPortal(
          <div className="pmv-img-overlay" onClick={handleBackdropClick}>
            <button className="pmv-img-overlay-close" onClick={handleCloseOverlay} aria-label="Close">
              ×
            </button>
            <img src={src} alt={alt} title={title} />
          </div>,
          document.body
        )}
    </>
  );
};
