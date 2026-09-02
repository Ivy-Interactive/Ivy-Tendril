import { describe, expect, it } from "vitest";
import {
  formatCountTick,
  formatCurrencyTick,
  niceTicks,
  rampLevel,
} from "./types";

describe("rampLevel", () => {
  it("returns 0 for empty values", () => {
    expect(rampLevel(0, 10)).toBe(0);
    expect(rampLevel(5, 0)).toBe(0);
  });

  it("maps intensity quarters onto the four-step ramp", () => {
    expect(rampLevel(1, 100)).toBe(1);
    expect(rampLevel(25, 100)).toBe(1);
    expect(rampLevel(40, 100)).toBe(2);
    expect(rampLevel(70, 100)).toBe(3);
    expect(rampLevel(100, 100)).toBe(4);
  });
});

describe("niceTicks", () => {
  it("covers the maximum with rounded steps", () => {
    expect(niceTicks(118, 3)).toEqual([0, 50, 100, 150]);
    expect(niceTicks(31400, 4)).toEqual([0, 10000, 20000, 30000, 40000]);
  });

  it("handles zero and tiny ranges", () => {
    expect(niceTicks(0)).toEqual([0, 1]);
    expect(niceTicks(3, 3)).toEqual([0, 1, 2, 3]);
  });
});

describe("tick formatters", () => {
  it("abbreviates thousands as currency", () => {
    expect(formatCurrencyTick(30000)).toBe("$30K");
    expect(formatCurrencyTick(40)).toBe("$40");
    expect(formatCurrencyTick(0)).toBe("0");
  });

  it("abbreviates thousands as counts", () => {
    expect(formatCountTick(1500)).toBe("2K");
    expect(formatCountTick(150)).toBe("150");
  });
});
