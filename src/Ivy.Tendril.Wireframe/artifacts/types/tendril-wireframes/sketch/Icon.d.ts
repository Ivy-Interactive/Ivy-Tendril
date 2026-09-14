import { icons, LucideProps } from 'lucide-react';
export type IconName = keyof typeof icons | (string & {});
export declare function lookupIcon(name?: string | null): import('react').ForwardRefExoticComponent<Omit<LucideProps, "ref"> & import('react').RefAttributes<SVGSVGElement>> | null;
export interface IconProps extends Omit<LucideProps, "ref" | "color" | "name"> {
    /** Lucide icon name, matching Ivy's `Icons` enum (e.g. `"Rocket"`). */
    name?: string | null;
    color?: string;
    size?: number;
    /** Set false to opt this icon out of the hand-drawn wobble filter. */
    wobble?: boolean;
}
/**
 * Lucide icons, pushed through a turbulence filter so they read as sketched
 * rather than vector-crisp.
 */
export declare const Icon: ({ name, color, size, wobble, className, ...rest }: IconProps) => import("react").JSX.Element | null;
