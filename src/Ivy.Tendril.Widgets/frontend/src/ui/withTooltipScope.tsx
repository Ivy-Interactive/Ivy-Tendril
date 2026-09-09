import React from "react";
import { TooltipScope } from "./Tooltip";

/**
 * Puts one tooltip scope at a widget's root, so the tooltips inside it hand over to each other
 * instead of each waiting out the open delay again. Applied to the widgets that host rows of
 * them; a widget mounted inside another's scope reuses that one.
 */
export function withTooltipScope<P extends object>(
  Widget: React.ComponentType<P>,
): React.FC<P> {
  const Scoped: React.FC<P> = (props) => (
    <TooltipScope>
      <Widget {...props} />
    </TooltipScope>
  );
  Scoped.displayName = Widget.displayName || Widget.name;
  return Scoped;
}
