import { ContentInputView } from "./ContentInputView";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).Ivy_Widgets_ContentInputView = {
    ContentInputView,
  };
}

export { ContentInputView };
