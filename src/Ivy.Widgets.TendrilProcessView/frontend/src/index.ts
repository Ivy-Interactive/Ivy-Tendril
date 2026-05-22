import { TendrilProcessView } from "./TendrilProcessView";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).Ivy_Widget_TendrilProcessView = {
    TendrilProcessView,
  };
}

export { TendrilProcessView };
