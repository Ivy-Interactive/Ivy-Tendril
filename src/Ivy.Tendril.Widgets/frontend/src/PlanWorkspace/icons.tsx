import React from "react";
import {
  Ban,
  CircleCheck,
  ClipboardCopy,
  Code,
  Copy,
  Ellipsis,
  EllipsisVertical,
  Expand,
  ExternalLink,
  FileText,
  FolderOpen,
  Github,
  GitPullRequest,
  LucideIcon,
  MessageSquare,
  Pencil,
  RotateCcw,
  Rocket,
  Save,
  Scissors,
  Share,
  Share2,
  Sparkles,
  Terminal,
  Trash,
  Trash2,
  UnfoldVertical,
  WandSparkles,
  X,
} from "lucide-react";
import { BrandIcon } from "../Shell/brandIcons";

/* The action bar renders the C# `Icons` names the plan apps use; anything else is tried as an
   agent brand mark (the "Discuss with Claude Code" menu item) and otherwise left blank. */
const lucideIcons: Record<string, LucideIcon> = {
  Ban,
  CircleCheck,
  ClipboardCopy,
  Code,
  Copy,
  Ellipsis,
  EllipsisVertical,
  Expand,
  ExternalLink,
  FileText,
  FolderOpen,
  Github,
  GitPullRequest,
  MessageSquare,
  Pencil,
  RotateCcw,
  Rocket,
  Save,
  Scissors,
  Share,
  Share2,
  Sparkles,
  Terminal,
  Trash,
  Trash2,
  UnfoldVertical,
  WandSparkles,
  X,
};

export const ActionIcon: React.FC<{ icon?: string; size?: number; className?: string }> = ({
  icon,
  size = 16,
  className,
}) => {
  if (!icon) return null;
  const Icon = lucideIcons[icon];
  if (Icon) return <Icon size={size} className={className} />;
  return <BrandIcon name={icon} size={size} className={className} />;
};
