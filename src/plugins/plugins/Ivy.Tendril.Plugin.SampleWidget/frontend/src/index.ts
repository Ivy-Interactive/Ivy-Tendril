import { Counter } from "./Counter";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).Ivy_Tendril_Plugin_SampleWidget = {
    Counter,
  };
}

export { Counter };
