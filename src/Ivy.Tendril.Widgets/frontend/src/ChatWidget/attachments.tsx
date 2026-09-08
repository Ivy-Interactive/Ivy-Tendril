import React, { useEffect, useRef, useState } from "react";
import { FileText, Paperclip, X } from "lucide-react";
import * as pdfjsLib from "pdfjs-dist";
import pdfjsWorker from "pdfjs-dist/build/pdf.worker.mjs?url";
import { isImageFile } from "../imageUtils";
import { getIvyHost } from "../PlanMarkdown/localFiles";
import type { ChatAttachmentDto } from "./types";

if (typeof window !== "undefined") {
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfjsWorker;
}

export const MAX_PAYLOAD_BYTES = 50 * 1024 * 1024;

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export const isPdfFile = (nameOrType: string): boolean => {
  const lower = nameOrType.toLowerCase();
  return lower === "application/pdf" || lower.endsWith(".pdf");
};

export const getAttachmentUrl = (filePath: string): string => {
  if (
    filePath.startsWith("http://") ||
    filePath.startsWith("https://") ||
    filePath.startsWith("data:") ||
    filePath.startsWith("blob:")
  ) {
    return filePath;
  }
  return `${getIvyHost()}/ivy/local-file?path=${encodeURIComponent(filePath)}`;
};

export const getFileExtBadge = (name: string): string => {
  const ext = name.split(".").pop()?.toUpperCase() || "FILE";
  return ext.length > 5 ? ext.slice(0, 5) : ext;
};

const ATTACHED_FILES_MARKER = "[Attached Files]:";

/** Splits a stored user message into its prompt and the file paths the backend appended to it. */
export const parseUserMessageContent = (content: string): { prompt: string; attachedPaths: string[] } => {
  if (!content) return { prompt: "", attachedPaths: [] };
  const markerIndex = content.indexOf(ATTACHED_FILES_MARKER);
  if (markerIndex === -1) return { prompt: content, attachedPaths: [] };

  const prompt = content.substring(0, markerIndex).trim();
  const attachedPaths = content
    .substring(markerIndex + ATTACHED_FILES_MARKER.length)
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.startsWith("- "))
    .map((line) => line.substring(2).trim())
    .filter(Boolean);
  return { prompt, attachedPaths };
};

export const PdfThumbnail: React.FC<{ url: string }> = ({ url }) => {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [error, setError] = useState(false);

  useEffect(() => {
    let active = true;

    const renderPdf = async () => {
      try {
        const pdf = await pdfjsLib.getDocument({ url }).promise;
        if (!active) return;
        const page = await pdf.getPage(1);
        if (!active) return;

        const canvas = canvasRef.current;
        if (!canvas) return;
        const context = canvas.getContext("2d");
        if (!context) return;

        const unscaledViewport = page.getViewport({ scale: 1.0 });
        const baseScale = Math.max(140 / unscaledViewport.width, 105 / unscaledViewport.height);
        const viewport = page.getViewport({ scale: baseScale * 3 });

        canvas.width = viewport.width;
        canvas.height = viewport.height;

        await page.render({ canvasContext: context, viewport, canvas }).promise;
      } catch (err) {
        console.error("PDF.js render failed:", err);
        if (active) setError(true);
      }
    };

    renderPdf();

    return () => {
      active = false;
    };
  }, [url]);

  if (error) {
    return (
      <div className="chat-thumbnail-fallback">
        <FileText size={22} />
      </div>
    );
  }

  return <canvas ref={canvasRef} className="chat-thumbnail-canvas" />;
};

interface ComposerAttachmentCardProps {
  attachment: ChatAttachmentDto;
  onRemove: () => void;
}

/** A file waiting in the composer: a preview for images and PDFs, name and size for the rest. */
export const ComposerAttachmentCard: React.FC<ComposerAttachmentCardProps> = ({ attachment: att, onRemove }) => {
  const isImage = isImageFile(att.contentType || att.name);
  const isPdf = isPdfFile(att.contentType || att.name);
  const previewSrc =
    att.previewUrl || (att.base64Data && att.base64Data.startsWith("data:") ? att.base64Data : undefined);
  const hasPreview = Boolean(previewSrc && (isImage || isPdf));
  const metaText = att.lineCount !== undefined ? `${att.lineCount} lines` : formatFileSize(att.size);

  return (
    <div className={`chat-thumbnail-card ${att.uploadStatus === "failed" ? "upload-failed" : ""}`} title={att.name}>
      {hasPreview && (
        <div className="chat-thumbnail-preview-container">
          {isImage ? (
            <img className="chat-thumbnail-image-preview" src={previewSrc} alt={att.name} />
          ) : (
            <PdfThumbnail url={previewSrc!} />
          )}
          <div className="chat-thumbnail-preview-overlay" />
        </div>
      )}

      {att.uploadStatus === "uploading" && (
        <div className="chat-thumbnail-uploading-overlay">
          <div className="chat-thumbnail-progress-bar-container">
            <div className="chat-thumbnail-progress-bar" style={{ width: `${att.uploadProgress ?? 0}%` }} />
          </div>
          <span className="chat-thumbnail-progress-text">{att.uploadProgress ?? 0}%</span>
        </div>
      )}

      {att.uploadStatus === "failed" && (
        <div className="chat-thumbnail-failed-badge" title={att.error || "Upload failed"}>
          Failed
        </div>
      )}

      <button
        type="button"
        className="chat-thumbnail-card-remove"
        onClick={onRemove}
        title="Remove file"
        aria-label="Remove attachment"
      >
        <X size={12} />
      </button>

      <div className="chat-thumbnail-content">
        {hasPreview ? (
          <div />
        ) : (
          <div style={{ minWidth: 0 }}>
            <div className="chat-thumbnail-doc-name" title={att.name}>
              {att.name}
            </div>
            <div className="chat-thumbnail-doc-meta">{metaText}</div>
          </div>
        )}
        <div className="chat-thumbnail-doc-badge">{getFileExtBadge(att.name)}</div>
      </div>
    </div>
  );
};

interface MessageAttachmentChipProps {
  filePath: string;
  onOpenImage: (url: string, title: string) => void;
}

/** A file that travelled with a sent message, as a chip inside the bubble. Images open a lightbox. */
export const MessageAttachmentChip: React.FC<MessageAttachmentChipProps> = ({ filePath, onOpenImage }) => {
  const fileName = filePath.split(/[/\\]/).pop() || filePath;
  const ext = fileName.split(".").pop()?.toUpperCase() || "FILE";
  const fileUrl = getAttachmentUrl(filePath);

  if (isImageFile(filePath)) {
    const open = () => onOpenImage(fileUrl, fileName);
    return (
      <div
        className="chat-attachment-chip chat-user-attachment-card chat-user-attachment-card-clickable"
        title={filePath}
        role="button"
        tabIndex={0}
        onClick={open}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            open();
          }
        }}
      >
        <img src={fileUrl} alt={fileName} className="chat-attachment-chip-thumb chat-user-attachment-card-image" />
        <span className="chat-attachment-chip-name chat-user-attachment-name">{fileName}</span>
        <span className="chat-attachment-chip-ext chat-user-attachment-ext">{ext}</span>
      </div>
    );
  }

  if (isPdfFile(filePath)) {
    return (
      <div className="chat-attachment-chip chat-user-attachment-card chat-user-attachment-card-pdf" title={filePath}>
        <FileText size={16} className="chat-attachment-chip-icon" />
        <span className="chat-attachment-chip-name chat-user-attachment-name">{fileName}</span>
        <span className="chat-attachment-chip-ext chat-user-attachment-ext">PDF</span>
      </div>
    );
  }

  return (
    <div className="chat-attachment-chip chat-user-attachment-badge" title={filePath}>
      <Paperclip size={16} className="chat-attachment-chip-icon chat-user-attachment-icon" />
      <span className="chat-attachment-chip-name chat-user-attachment-name">{fileName}</span>
      <span className="chat-attachment-chip-ext chat-user-attachment-ext">{ext}</span>
    </div>
  );
};
