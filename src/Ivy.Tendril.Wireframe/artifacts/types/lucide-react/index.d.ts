// Minimal ambient shim -- lucide-react's own types are large and mostly icon names.
import type { FC, SVGProps } from "react";
export interface LucideProps extends SVGProps<SVGSVGElement> {
  size?: string | number;
  absoluteStrokeWidth?: boolean;
}
export type LucideIcon = FC<LucideProps>;
declare const icons: Record<string, LucideIcon>;
export { icons };
export const _default: Record<string, LucideIcon>;
export default _default;
