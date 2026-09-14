import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
export interface EmbedProps extends WidgetBaseProps {
    url: string;
}
/**
 * Wireframes stand in for third-party embeds rather than loading them — a card
 * naming the platform and the link. Mirrors `Ivy.Embed`.
 */
export declare const Embed: ({ id, url, width, height, aspectRatio, visible, className, style, }: EmbedProps) => React.JSX.Element;
export interface AudioPlayerProps extends WidgetBaseProps {
    src?: string | null;
    autoplay?: boolean;
    loop?: boolean;
    muted?: boolean;
    preload?: "none" | "metadata" | "auto";
    controls?: boolean;
}
/** Mirrors `Ivy.AudioPlayer`. */
export declare const AudioPlayer: ({ id, src, width, height, aspectRatio, visible, autoplay, loop, muted, preload, controls, className, style, ...rest }: AudioPlayerProps) => React.JSX.Element;
export interface VideoPlayerProps extends WidgetBaseProps {
    source?: string | null;
    autoplay?: boolean;
    loop?: boolean;
    muted?: boolean;
    preload?: "none" | "metadata" | "auto";
    controls?: boolean;
    poster?: string;
    volume?: number;
    startTime?: number;
    endTime?: number;
    playbackRate?: number;
    subtitles?: Array<{
        source: string;
        label?: string;
    }>;
    onEnded?: () => void;
    onPlay?: () => void;
    onPause?: () => void;
}
/** Mirrors `Ivy.VideoPlayer`. */
export declare const VideoPlayer: ({ id, source, width, height, aspectRatio, visible, autoplay, loop, muted, preload, controls, poster, volume, startTime, endTime, playbackRate, subtitles, className, style, onEnded, onPlay, onPause, }: VideoPlayerProps) => React.JSX.Element;
