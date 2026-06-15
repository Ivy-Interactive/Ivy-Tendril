import React, { useState, useRef, useEffect, useCallback } from "react";
import { createPortal } from "react-dom";

interface Position {
  top: number;
  left: number;
}

interface AddAnnotationPopoverProps {
  position: Position;
  selectedText: string;
  onAdd: (comment: string) => void;
  onCancel: () => void;
}

export const AddAnnotationPopover: React.FC<AddAnnotationPopoverProps> = ({
  position,
  selectedText,
  onAdd,
  onCancel,
}) => {
  const [comment, setComment] = useState("");
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    textareaRef.current?.focus();
  }, []);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
        onCancel();
      }
    };
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
    };
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleEscape);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleEscape);
    };
  }, [onCancel]);

  const handleSubmit = useCallback(() => {
    onAdd(comment);
  }, [comment, onAdd]);

  return createPortal(
    <div ref={popoverRef} className="pmv-popover" style={{ top: position.top, left: position.left }}>
      <div className="pmv-popover-quote">
        &ldquo;{selectedText.slice(0, 50)}
        {selectedText.length > 50 ? "..." : ""}&rdquo;
      </div>
      <textarea
        ref={textareaRef}
        className="pmv-popover-textarea"
        rows={3}
        placeholder="Add a comment..."
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
            e.preventDefault();
            handleSubmit();
          }
        }}
      />
      <div className="pmv-popover-actions pmv-popover-actions--end">
        <button type="button" className="pmv-popover-btn pmv-popover-btn--ghost" onClick={onCancel}>
          Cancel
        </button>
        <button type="button" className="pmv-popover-btn pmv-popover-btn--primary" onClick={handleSubmit}>
          Add
        </button>
      </div>
    </div>,
    document.body,
  );
};

interface EditAnnotationPopoverProps {
  position: Position;
  annotation: { selectedText: string; comment: string };
  onSave: (comment: string) => void;
  onRemove: () => void;
  onCancel: () => void;
}

export const EditAnnotationPopover: React.FC<EditAnnotationPopoverProps> = ({
  position,
  annotation,
  onSave,
  onRemove,
  onCancel,
}) => {
  const [comment, setComment] = useState(annotation.comment);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    textareaRef.current?.focus();
  }, []);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
        onCancel();
      }
    };
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
    };
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleEscape);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleEscape);
    };
  }, [onCancel]);

  const handleSave = useCallback(() => {
    onSave(comment);
  }, [comment, onSave]);

  return createPortal(
    <div ref={popoverRef} className="pmv-popover" style={{ top: position.top, left: position.left }}>
      <div className="pmv-popover-quote">
        &ldquo;{annotation.selectedText.slice(0, 50)}
        {annotation.selectedText.length > 50 ? "..." : ""}&rdquo;
      </div>
      <textarea
        ref={textareaRef}
        className="pmv-popover-textarea"
        rows={3}
        placeholder="Edit comment..."
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.metaKey || e.ctrlKey)) {
            e.preventDefault();
            handleSave();
          }
        }}
      />
      <div className="pmv-popover-actions pmv-popover-actions--between">
        <button type="button" className="pmv-popover-btn pmv-popover-btn--danger" onClick={onRemove}>
          Remove
        </button>
        <div className="pmv-popover-actions pmv-popover-actions--end">
          <button type="button" className="pmv-popover-btn pmv-popover-btn--ghost" onClick={onCancel}>
            Cancel
          </button>
          <button type="button" className="pmv-popover-btn pmv-popover-btn--primary" onClick={handleSave}>
            Save
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
};

interface SelectionToolbarProps {
  position: Position;
  onAddComment: () => void;
}

const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform);
const ADD_COMMENT_SHORTCUT = isMac ? "⌘⌥M" : "Ctrl+Alt+M";

export const SelectionToolbar: React.FC<SelectionToolbarProps> = ({ position, onAddComment }) => {
  return createPortal(
    <div className="pmv-selection-toolbar" style={{ top: position.top, left: position.left }}>
      <button
        type="button"
        className="pmv-selection-toolbar-btn"
        onClick={onAddComment}
        title={`Add Comment (${ADD_COMMENT_SHORTCUT})`}
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="12"
          height="12"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="m9 11-6 6v3h9l3-3" />
          <path d="m22 12-4.6 4.6a2 2 0 0 1-2.8 0l-5.2-5.2a2 2 0 0 1 0-2.8L14 4" />
        </svg>
        Add Comment
        <kbd className="pmv-selection-toolbar-kbd">{ADD_COMMENT_SHORTCUT}</kbd>
      </button>
    </div>,
    document.body,
  );
};
