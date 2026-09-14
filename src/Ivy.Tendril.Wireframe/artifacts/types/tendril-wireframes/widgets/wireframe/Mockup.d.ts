import { WidgetBaseProps } from '../../lib/types';
import * as React from "react";
/**
 * Which frame to draw.
 *
 * `OSX` and `Windows` are separate on purpose. They were one `Desktop` variant drawing
 * macOS traffic lights, which is actively misleading on a wireframe of a Windows
 * application — the platform is usually the first thing a reviewer reads off a window
 * sketch, and there was no way to say "Windows" at all.
 */
export type MockupVariant = 
/** A phone: bezel, speaker slot, side buttons, home indicator. */
"Mobile"
/** A tablet: thinner bezel, a camera, no side buttons. */
 | "Tablet"
/** A browser window: toolbar, nav glyphs, address pill. */
 | "Website"
/** A macOS application window: traffic lights left, title centred. */
 | "OSX"
/** A Windows application window: app icon and title left, caption buttons right. */
 | "Windows";
/** Space the chrome takes on each edge, so the content can be laid out inside it. */
interface Insets {
    top: number;
    right: number;
    bottom: number;
    left: number;
}
/** Which optional rows of chrome a mockup was given. */
export interface MockupRows {
    menu?: boolean;
    footer?: boolean;
}
/**
 * How much room the chrome needs, before anything has been measured.
 *
 * The frame is drawn from the measured box, but the content has to be laid out *inside* the
 * chrome for the mockup to grow with it — which means knowing the insets first. They depend
 * only on the variant, the pencil weight and which rows are present, so this is a pure
 * function and the two can never disagree.
 */
export declare function mockupInsets(variant: MockupVariant, sw: number, rows?: MockupRows): Insets;
export interface WireframeMockupProps extends WidgetBaseProps {
    variant?: MockupVariant;
    color?: string;
    /**
     * Window title. Drawn left of centre on `Windows` (beside the icon) and centred on `OSX`.
     * Ignored by the device and browser variants.
     */
    title?: string;
    /**
     * Icon name from the lucide set. The app icon in a `Windows` title bar, beside the title
     * on `OSX`, and the favicon in the `Website` address pill.
     */
    icon?: string;
    /**
     * Menu bar items, drawn as a row of chrome under the title bar — `["File", "Edit", "View"]`.
     * Real chrome rather than a hand-laid row of labels inside the content.
     */
    menu?: string[];
    /** Address bar text on the `Website` variant. Ignored by the other variants. */
    url?: string;
    /**
     * A status bar along the bottom, inside the frame. Takes any node, so it can hold a row of
     * counts, a progress bar, or plain text.
     */
    footer?: React.ReactNode;
    /**
     * Laid out inside the frame's screen area, so a mockup can hold anything.
     *
     * The frame grows to fit this. It only ever clips when `height` is set, and then the cut
     * is drawn as a torn edge rather than left silent.
     */
    children?: React.ReactNode;
}
/**
 * A hand-drawn device or browser frame wrapped around real content.
 * Mirrors `Ivy.WireframeMockup`.
 *
 * **It grows to fit its children.** The variant's size is a starting point, not a limit, so
 * a long page inside a `Website` frame makes the frame taller rather than being cut off at
 * the bottom of a notional screen.
 *
 * **Set `height` only when the frame's size is the point** — showing that content overflows
 * a phone, or lining two mockups up. Content that then does not fit is clipped, and the clip
 * is drawn as a torn edge across the foot of the screen, because a mockup that silently
 * loses half its content is worse than one that admits it.
 *
 * `OSX` and `Windows` draw their own platform's chrome: traffic lights and a centred title
 * for one, an app icon, a left title and caption buttons for the other. `title`, `icon`,
 * `menu` and `footer` build the rest of the window.
 *
 * @tags device phone browser frame chrome window desktop
 * @slot children Laid out inside the frame's screen area; the frame grows to fit them
 * @slot footer A status bar along the bottom, inside the frame
 * @example <WireframeMockup variant="Mobile"><Card title="Inbox" /></WireframeMockup>
 * @example <WireframeMockup variant="Website" url="app.example.com" icon="Globe" />
 * @example <WireframeMockup variant="Windows" title="Convertly" icon="Repeat" menu={["File", "Edit", "View", "Help"]} footer="Ready" />
 * @example <WireframeMockup variant="OSX" title="Preview" footer="3 items" />
 */
export declare const WireframeMockup: ({ id, variant, color, title, icon, menu, url, footer, children, density, width, height, aspectRatio, visible, className, style, }: WireframeMockupProps) => React.JSX.Element;
export {};
