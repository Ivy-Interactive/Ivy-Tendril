export type DeviceKey = "desktop" | "tablet" | "mobile";

export const DEVICE_ORDER: DeviceKey[] = ["desktop", "tablet", "mobile"];

export const DEVICE_LABELS: Record<DeviceKey, string> = {
  desktop: "Desktop",
  tablet: "Tablet",
  mobile: "Mobile",
};

export const DEVICE_VIEWPORTS: Record<DeviceKey, { w: number | null; h: number | null }> = {
  desktop: { w: null, h: null },
  tablet: { w: 820, h: 1180 },
  mobile: { w: 390, h: 844 },
};

export function toDeviceKey(value?: string | null): DeviceKey {
  const key = (value || "desktop").toLowerCase();
  return key === "tablet" || key === "mobile" ? key : "desktop";
}
