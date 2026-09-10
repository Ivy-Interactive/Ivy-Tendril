import React, { useCallback, useEffect, useMemo, useState } from "react";
import { ChevronDown, ChevronRight, File, FileCode, FileMinus, FilePlus, FileText, Folder } from "lucide-react";
import { PlanDiffView, getBasename, useIsNarrow, type DraftComment, type IvyEventHandler } from "./PlanDiffView";
import { getWidth, getHeight } from "../styles";
import "./plan-diff.css";

export interface ChangedFile {
  filePath: string;
  status: string;
  diff: string;
  additions: number;
  deletions: number;
}

interface PlanChangesViewProps {
  id: string;
  width?: string;
  height?: string;
  eventHandler?: IvyEventHandler;
  onIvyEvent?: IvyEventHandler;
  events?: string[];
  files?: ChangedFile[];
  comments?: DraftComment[];
  currentAuthor?: string;
}

export interface TreeFolder {
  name: string;
  path: string;
  folders: TreeFolder[];
  files: ChangedFile[];
}

const INDENT_PX = 12;

const CODE_EXTENSIONS = new Set([
  "cs", "js", "cjs", "mjs", "jsx", "ts", "mts", "cts", "tsx", "py", "html", "htm", "css", "scss",
  "json", "xml", "csproj", "props", "targets", "sh", "bash", "zsh", "yaml", "yml", "sql", "c", "h",
  "cpp", "hpp", "cc", "cxx", "java", "go", "rs", "rb", "php", "kt", "swift", "toml", "ps1",
]);
const TEXT_EXTENSIONS = new Set(["md", "markdown", "txt", "rst"]);

function compareNames(a: string, b: string): number {
  const left = a.toUpperCase();
  const right = b.toUpperCase();
  return left < right ? -1 : left > right ? 1 : 0;
}

function normalizePath(path: string): string {
  return path.replace(/\\/g, "/");
}

export function buildFileTree(files: ChangedFile[]): TreeFolder {
  const root: TreeFolder = { name: "", path: "", folders: [], files: [] };
  for (const file of files) {
    const segments = normalizePath(file.filePath).split("/").filter(Boolean);
    let node = root;
    for (let i = 0; i < segments.length - 1; i++) {
      const segment = segments[i];
      let child = node.folders.find((f) => compareNames(f.name, segment) === 0);
      if (!child) {
        child = { name: segment, path: node.path ? `${node.path}/${segment}` : segment, folders: [], files: [] };
        node.folders.push(child);
      }
      node = child;
    }
    node.files.push(file);
  }
  sortTree(root);
  return root;
}

function sortTree(node: TreeFolder) {
  node.folders.sort((a, b) => compareNames(a.name, b.name));
  node.files.sort((a, b) => compareNames(getBasename(a.filePath), getBasename(b.filePath)));
  for (const folder of node.folders) sortTree(folder);
}

export function flattenTreeOrder(node: TreeFolder): ChangedFile[] {
  const result: ChangedFile[] = [];
  for (const folder of node.folders) result.push(...flattenTreeOrder(folder));
  result.push(...node.files);
  return result;
}

export function collapseFolderChain(folder: TreeFolder): { label: string; node: TreeFolder } {
  let label = folder.name;
  let node = folder;
  while (node.files.length === 0 && node.folders.length === 1) {
    const only = node.folders[0];
    label = `${label}/${only.name}`;
    node = only;
  }
  return { label, node };
}

type StatusTone = "success" | "destructive" | "neutral";

function folderTone(node: TreeFolder): StatusTone | null {
  let hasAdded = false;
  let hasDeleted = false;
  let hasOther = false;
  const visit = (n: TreeFolder) => {
    for (const f of n.files) {
      if (f.status === "A") hasAdded = true;
      else if (f.status === "D") hasDeleted = true;
      else hasOther = true;
    }
    for (const folder of n.folders) visit(folder);
  };
  visit(node);
  if (!hasAdded && !hasDeleted && !hasOther) return null;
  if (hasAdded && !hasDeleted && !hasOther) return "success";
  if (hasDeleted && !hasAdded && !hasOther) return "destructive";
  return "neutral";
}

function toneClass(tone: StatusTone | null): string {
  switch (tone) {
    case "success":
      return "text-[var(--success)]";
    case "destructive":
      return "text-[var(--destructive)]";
    default:
      return "text-[var(--muted-foreground)]";
  }
}

function FileIcon({ file }: { file: ChangedFile }) {
  const className = `ivy-changes-tree-icon ${toneClass(file.status === "A" ? "success" : file.status === "D" ? "destructive" : null)}`;
  if (file.status === "A") return <FilePlus className={className} />;
  if (file.status === "D") return <FileMinus className={className} />;
  const ext = getBasename(file.filePath).split(".").pop()?.toLowerCase() || "";
  if (CODE_EXTENSIONS.has(ext)) return <FileCode className={className} />;
  if (TEXT_EXTENSIONS.has(ext)) return <FileText className={className} />;
  return <File className={className} />;
}

function FileStats({ additions, deletions }: { additions: number; deletions: number }) {
  if (additions <= 0 && deletions <= 0) return null;
  return (
    <span className="ivy-changes-tree-stats">
      {additions > 0 && <span className="text-[var(--success)]">+{additions}</span>}
      {deletions > 0 && <span className="text-[var(--destructive)]">-{deletions}</span>}
    </span>
  );
}

interface TreeRowsProps {
  node: TreeFolder;
  depth: number;
  selectedPath: string | null;
  collapsed: Record<string, boolean>;
  onToggleFolder: (path: string) => void;
  onSelectFile: (path: string) => void;
}

function TreeRows({ node, depth, selectedPath, collapsed, onToggleFolder, onSelectFile }: TreeRowsProps) {
  return (
    <>
      {node.folders.map((folder) => {
        const { label, node: target } = collapseFolderChain(folder);
        const isCollapsed = collapsed[target.path] ?? false;
        const Chevron = isCollapsed ? ChevronRight : ChevronDown;
        return (
          <React.Fragment key={target.path}>
            <div
              role="treeitem"
              aria-expanded={!isCollapsed}
              aria-level={depth + 1}
              tabIndex={0}
              className="ivy-changes-tree-row"
              style={{ paddingLeft: depth * INDENT_PX }}
              title={target.path}
              onClick={() => onToggleFolder(target.path)}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  onToggleFolder(target.path);
                }
              }}
            >
              <Chevron className="ivy-changes-tree-chevron" />
              <Folder className={`ivy-changes-tree-icon ${toneClass(folderTone(target))}`} />
              <span className="ivy-changes-tree-name">{label}</span>
            </div>
            {!isCollapsed && (
              <TreeRows
                node={target}
                depth={depth + 1}
                selectedPath={selectedPath}
                collapsed={collapsed}
                onToggleFolder={onToggleFolder}
                onSelectFile={onSelectFile}
              />
            )}
          </React.Fragment>
        );
      })}
      {node.files.map((file) => {
        const isSelected = selectedPath === file.filePath;
        return (
          <div
            key={file.filePath}
            role="treeitem"
            aria-selected={isSelected}
            aria-level={depth + 1}
            tabIndex={0}
            className={`ivy-changes-tree-row${isSelected ? " ivy-changes-tree-row-selected" : ""}`}
            style={{ paddingLeft: depth * INDENT_PX }}
            title={file.filePath}
            onClick={() => onSelectFile(file.filePath)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onSelectFile(file.filePath);
              }
            }}
          >
            <span className="ivy-changes-tree-chevron" aria-hidden="true" />
            <FileIcon file={file} />
            <span className="ivy-changes-tree-name">{getBasename(file.filePath)}</span>
            <FileStats additions={file.additions} deletions={file.deletions} />
          </div>
        );
      })}
    </>
  );
}

export const PlanChangesView: React.FC<PlanChangesViewProps> = ({
  id,
  width,
  height,
  eventHandler,
  onIvyEvent,
  files = [],
  comments = [],
  currentAuthor,
}) => {
  const dispatchEvent = eventHandler || onIvyEvent;
  const [containerRef, isNarrow] = useIsNarrow();
  const [selectedPath, setSelectedPath] = useState<string | null>(null);
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});

  const tree = useMemo(() => buildFileTree(files), [files]);
  const orderedFiles = useMemo(() => flattenTreeOrder(tree), [tree]);

  useEffect(() => {
    setSelectedPath(null);
    setCollapsed({});
  }, [id]);

  const commentsByFile = useMemo(() => {
    const map: Record<string, DraftComment[]> = {};
    for (const c of comments) {
      (map[c.filePath] ??= []).push(c);
    }
    return map;
  }, [comments]);

  const toggleFolder = useCallback((path: string) => {
    setCollapsed((prev) => ({ ...prev, [path]: !(prev[path] ?? false) }));
  }, []);

  const selectFile = useCallback((path: string) => {
    setSelectedPath(path);
    if (typeof document === "undefined") return;
    document.getElementById(path)?.scrollIntoView({ block: "start" });
  }, []);

  const style: React.CSSProperties = {
    ...getWidth(width),
    ...getHeight(height),
  };

  if (orderedFiles.length === 0) {
    return (
      <div ref={containerRef} style={style} className="text-[var(--muted-foreground)] p-4 text-sm">
        No file changes.
      </div>
    );
  }

  return (
    <div ref={containerRef} style={style} className={`ivy-changes-view${isNarrow ? " ivy-changes-view-narrow" : ""}`}>
      {!isNarrow && (
        <div role="tree" aria-label="Changed files" className="ivy-changes-tree">
          <TreeRows
            node={tree}
            depth={0}
            selectedPath={selectedPath}
            collapsed={collapsed}
            onToggleFolder={toggleFolder}
            onSelectFile={selectFile}
          />
        </div>
      )}
      {isNarrow && (
        <div className="ivy-changes-jump">
          <span className="text-xs text-[var(--muted-foreground)] shrink-0">{orderedFiles.length} files</span>
          <select
            aria-label="Jump to file"
            className="flex-1 min-w-0 text-xs px-2 py-1 rounded bg-[var(--background)] text-[var(--foreground)] border border-[var(--border)]"
            value={selectedPath ?? ""}
            onChange={(e) => {
              if (e.target.value) selectFile(e.target.value);
            }}
          >
            <option value="" disabled>
              Jump to file…
            </option>
            {orderedFiles.map((file) => (
              <option key={file.filePath} value={file.filePath}>
                {file.filePath}
              </option>
            ))}
          </select>
        </div>
      )}
      <div className="ivy-changes-diffs">
        {orderedFiles.map((file) => (
          <PlanDiffView
            key={file.filePath}
            id={id}
            eventHandler={dispatchEvent}
            diff={file.diff}
            filePath={file.filePath}
            collapsible
            comments={commentsByFile[file.filePath] ?? []}
            currentAuthor={currentAuthor}
          />
        ))}
      </div>
    </div>
  );
};
