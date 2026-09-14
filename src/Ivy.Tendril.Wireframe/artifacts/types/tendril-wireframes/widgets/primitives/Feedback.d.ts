import { WidgetBaseProps } from '../../lib/types';
export interface SkeletonProps extends WidgetBaseProps {
    /** Number of stacked placeholder lines. */
    lines?: number;
}
/** A scribbled placeholder block. Mirrors `Ivy.Skeleton`. */
export declare const Skeleton: ({ id, width, height, aspectRatio, visible, lines, className, style, }: SkeletonProps) => import("react").JSX.Element;
export interface LoadingProps extends WidgetBaseProps {
    type?: "Spinner" | "Skeleton";
    label?: string;
}
/** Spinner or skeleton placeholder. Mirrors `Ivy.Loading`. */
export declare const Loading: ({ id, type, label, width, height, aspectRatio, visible, className, style, }: LoadingProps) => import("react").JSX.Element;
