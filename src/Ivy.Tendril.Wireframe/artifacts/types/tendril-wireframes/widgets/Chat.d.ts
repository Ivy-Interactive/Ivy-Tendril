import { WidgetBaseProps } from '../lib/types';
import * as React from "react";
export interface ChatMessageProps extends WidgetBaseProps {
    sender?: "User" | "Assistant";
    children?: React.ReactNode;
}
/** One speech bubble. Mirrors `Ivy.ChatMessage`. */
export declare const ChatMessage: ({ id, sender, density, width, height, aspectRatio, visible, children, className, style, }: ChatMessageProps) => React.JSX.Element;
/** The three-dot "thinking" bubble. Mirrors `Ivy.ChatLoading`. */
export declare const ChatLoading: ({ id, width, height, aspectRatio, visible, className, style, }: WidgetBaseProps) => React.JSX.Element;
export interface ChatStatusProps extends WidgetBaseProps {
    text?: string;
}
/** A centred status line between messages. Mirrors `Ivy.ChatStatus`. */
export declare const ChatStatus: ({ id, text, width, height, aspectRatio, visible, className, style, }: ChatStatusProps) => React.JSX.Element;
export interface ChatProps extends WidgetBaseProps {
    children?: React.ReactNode;
    placeholder?: string;
    /** Disables the composer while a response streams in. */
    streaming?: boolean;
    onSend?: (message: string) => void;
}
/**
 * A transcript with a composer at the bottom. Mirrors `Ivy.Chat`.
 *
 * @tags conversation messages assistant
 * @example <Chat onSend={send}><ChatMessage sender="User">Hello</ChatMessage></Chat>
 */
export declare const Chat: ({ id, children, placeholder, streaming, width, height, aspectRatio, visible, density, className, style, onSend, }: ChatProps) => React.JSX.Element;
