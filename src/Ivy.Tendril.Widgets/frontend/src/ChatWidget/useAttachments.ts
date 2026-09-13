import { useCallback, useEffect, useRef, useState } from "react";
import { isImageFile, processImageFile } from "../imageUtils";
import type { ChatAttachmentDto } from "./types";
import { MAX_PAYLOAD_BYTES, isPdfFile } from "./attachments";

const TEXT_EXTENSIONS = [
  ".txt", ".log", ".json", ".csv", ".md", ".cs", ".ts", ".tsx", ".js", ".py", ".yaml", ".yml", ".xml", ".html",
];

const isTextLike = (mimeType: string, fileName: string): boolean =>
  mimeType.startsWith("text/") || TEXT_EXTENSIONS.some((ext) => fileName.endsWith(ext));

const countLines = async (file: File, mimeType: string, fileName: string): Promise<number | undefined> => {
  if (!isTextLike(mimeType, fileName) || typeof file.text !== "function") return undefined;
  try {
    return (await file.text()).split("\n").length;
  } catch {
    return undefined;
  }
};

const createPreviewUrl = (file: File, mimeType: string, fileName: string): string | undefined => {
  if (!isImageFile(mimeType || fileName) && !isPdfFile(mimeType || fileName)) return undefined;
  try {
    return typeof URL !== "undefined" && typeof URL.createObjectURL === "function"
      ? URL.createObjectURL(file)
      : undefined;
  } catch {
    return undefined;
  }
};

const revokePreview = (attachment: ChatAttachmentDto) => {
  if (!attachment.previewUrl) return;
  try {
    URL.revokeObjectURL(attachment.previewUrl);
  } catch {
    // nothing to release
  }
};

const readAsDataUrl = (file: File): Promise<string> =>
  new Promise((resolve) => {
    if (typeof FileReader === "undefined") {
      resolve("");
      return;
    }
    try {
      const reader = new FileReader();
      reader.onload = (evt) => resolve((evt.target?.result as string) || "");
      reader.onerror = () => resolve("");
      reader.readAsDataURL(file);
    } catch {
      resolve("");
    }
  });

/**
 * The composer's pending files. With an upload URL every file is posted as it is added and only
 * its metadata travels with the message; without one the file itself goes along, base64-encoded.
 */
export function useAttachments(uploadUrl?: string) {
  const [attachments, setAttachments] = useState<ChatAttachmentDto[]>([]);
  const attachmentsRef = useRef(attachments);

  useEffect(() => {
    attachmentsRef.current = attachments;
  }, [attachments]);

  useEffect(
    () => () => {
      attachmentsRef.current.forEach(revokePreview);
    },
    [],
  );

  const patch = useCallback((fileId: string, changes: Partial<ChatAttachmentDto>) => {
    setAttachments((prev) => prev.map((att) => (att.fileId === fileId ? { ...att, ...changes } : att)));
  }, []);

  const upload = useCallback(
    async (file: File, fileId: string, fileName: string) => {
      if (!uploadUrl) return;
      const formData = new FormData();
      formData.append("file", file, fileName);

      if (typeof XMLHttpRequest !== "undefined") {
        const xhr = new XMLHttpRequest();
        xhr.open("POST", uploadUrl, true);
        if (xhr.upload) {
          xhr.upload.onprogress = (evt) => {
            if (evt.lengthComputable) {
              patch(fileId, { uploadProgress: Math.round((evt.loaded / evt.total) * 100) });
            }
          };
        }
        xhr.onload = () => {
          if (xhr.status >= 200 && xhr.status < 300) {
            patch(fileId, { uploadStatus: "finished", uploadProgress: 100 });
          } else {
            patch(fileId, { uploadStatus: "failed", error: `Upload failed (status ${xhr.status})` });
          }
        };
        xhr.onerror = () => patch(fileId, { uploadStatus: "failed", error: "Upload failed: Network error" });
        xhr.send(formData);
        return;
      }

      if (typeof fetch !== "undefined") {
        try {
          const resp = await fetch(uploadUrl, { method: "POST", body: formData });
          if (resp.ok) {
            patch(fileId, { uploadStatus: "finished", uploadProgress: 100 });
          } else {
            patch(fileId, { uploadStatus: "failed", error: `Upload failed (status ${resp.status})` });
          }
        } catch (err) {
          patch(fileId, { uploadStatus: "failed", error: `Upload failed: ${err}` });
        }
      }
    },
    [uploadUrl, patch],
  );

  const addFiles = useCallback(
    async (filesList: FileList | File[]) => {
      const list = Array.from(filesList);
      if (list.length === 0) return;

      const added: ChatAttachmentDto[] = [];
      const pending: { file: File; fileId: string; fileName: string }[] = [];

      for (let i = 0; i < list.length; i++) {
        let file = list[i];
        if (isImageFile(file.type || file.name)) {
          try {
            file = await processImageFile(file);
          } catch {
            // keep the original file
          }
        }
        const mimeType = file.type || "application/octet-stream";
        const ext = mimeType.split("/")[1] || file.name?.split(".").pop() || "bin";
        const fileName =
          file.name && file.name.trim() !== "" && file.name !== "blob" ? file.name : `file_${Date.now()}_${i}.${ext}`;
        const fileId = `att-${Date.now()}-${i}-${Math.random().toString(36).substring(2, 9)}`;
        const lineCount = await countLines(file, mimeType, fileName);
        const previewUrl = createPreviewUrl(file, mimeType, fileName);
        const base = { name: fileName, contentType: mimeType, size: file.size || 0, lineCount, previewUrl, fileId };

        if (uploadUrl) {
          added.push({ ...base, uploadStatus: "uploading", uploadProgress: 0 });
          pending.push({ file, fileId, fileName });
        } else {
          added.push({ ...base, base64Data: await readAsDataUrl(file), uploadStatus: "finished", uploadProgress: 100 });
        }
      }

      setAttachments((prev) => [...prev, ...added]);

      for (const item of pending) {
        await upload(item.file, item.fileId, item.fileName);
      }
    },
    [uploadUrl, upload],
  );

  const removeAttachment = useCallback((index: number) => {
    setAttachments((prev) => {
      const target = prev[index];
      if (target) revokePreview(target);
      return prev.filter((_, i) => i !== index);
    });
  }, []);

  const clearAttachments = useCallback(() => {
    setAttachments((prev) => {
      prev.forEach(revokePreview);
      return [];
    });
  }, []);

  const totalSize = attachments.reduce((sum, att) => sum + (att.size || 0), 0);

  return {
    attachments,
    addFiles,
    removeAttachment,
    clearAttachments,
    totalSize,
    isPayloadOversized: totalSize > MAX_PAYLOAD_BYTES,
    isUploading: attachments.some((att) => att.uploadStatus === "uploading"),
    isAnyFailed: attachments.some((att) => att.uploadStatus === "failed"),
    hasValidAttachments: attachments.some((att) => att.uploadStatus === "finished" || !att.uploadStatus),
  };
}
