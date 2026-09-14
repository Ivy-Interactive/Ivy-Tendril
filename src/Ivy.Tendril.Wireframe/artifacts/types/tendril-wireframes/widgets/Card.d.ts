import { HoverEffect, WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface CardProps extends WidgetBaseProps {
    title?: string;
    description?: string;
    /** Rendered above the title area; use for toolbars, badges, avatars. */
    header?: React.ReactNode;
    footer?: React.ReactNode;
    children?: React.ReactNode;
    hoverVariant?: HoverEffect;
    disabled?: boolean;
    onClick?: React.MouseEventHandler<HTMLElement>;
}
/**
 * A sheet of paper with an optional header and footer. Mirrors `Ivy.Card`,
 * whose `Header` / `Content` / `Footer` slots become props here.
 *
 * @tags container panel surface
 * @slot header Rendered above the title
 * @slot footer Rendered below a dashed rule
 * @example <Card title="Revenue" description="Last 30 days">£24,500</Card>
 */
export declare const Card: ({ id, title, description, header, footer, children, width, height, aspectRatio, visible, hoverVariant, density, disabled, className, style, onClick, ...rest }: CardProps) => React.JSX.Element;
