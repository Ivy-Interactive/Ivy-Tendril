import { describe, expect, it } from "vitest";
import {
  computeActivityMetrics,
  computeAverage,
  computeRollingAverage,
  formatAxisDate,
  formatCountTick,
  formatCurrencyTick,
  formatTooltipDate,
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

describe("computeActivityMetrics", () => {
  it("returns 0 for empty months or months without weeks", () => {
    expect(computeActivityMetrics([])).toEqual({ totalPrs: 0, activeWeeks: 0 });
    expect(computeActivityMetrics([{ label: "Jan", weeks: [] }])).toEqual({
      totalPrs: 0,
      activeWeeks: 0,
    });
  });

  it("sums total merged PRs and counts weeks with merged PRs", () => {
    const months = [
      { label: "Jan", weeks: [0, 2, 0, 1] },
      { label: "Feb", weeks: [3, 0, 0, 0] },
      { label: "Mar", weeks: [0, 0, 0, 0] },
    ];
    expect(computeActivityMetrics(months)).toEqual({
      totalPrs: 6,
      activeWeeks: 3,
    });
  });
});

describe("computeAverage", () => {
  it("computes arithmetic mean for integer arrays", () => {
    expect(computeAverage([10, 20, 30])).toBe(20);
    expect(computeAverage([5])).toBe(5);
  });

  it("computes arithmetic mean for decimal arrays", () => {
    expect(computeAverage([1.5, 2.5, 5.0])).toBe(3);
  });

  it("handles arrays with zeroes", () => {
    expect(computeAverage([0, 0, 0])).toBe(0);
    expect(computeAverage([0, 10])).toBe(5);
  });

  it("returns null for empty arrays", () => {
    expect(computeAverage([])).toBeNull();
  });
});

describe("computeRollingAverage", () => {
  it("moves with the data instead of collapsing to one constant", () => {
    // The whole reason this replaced the average line: a flat result would be the same
    // non-information under a new name.
    const values = Array.from({ length: 28 }, (_, i) => (i % 11) * 10);

    const rolling = computeRollingAverage(values).slice(6) as number[];

    expect(new Set(rolling).size).toBeGreaterThan(1);
  });

  it("computes expanding average for the first six entries", () => {
    const rolling = computeRollingAverage([1, 2, 3, 4, 5, 6, 7, 8]);

    expect(rolling.slice(0, 6)).toEqual([1, 1.5, 2, 2.5, 3, 3.5]);
    expect(rolling[6]).toBe(4);
    expect(rolling[7]).toBe(5);
  });

  it("averages each entry with the six before it", () => {
    const values = Array.from({ length: 14 }, (_, i) => i + 1);

    const rolling = computeRollingAverage(values);

    expect(rolling[6]).toBe(4);
    expect(rolling[13]).toBe(11);
  });

  it("divides by the window so a quiet day pulls the mean down", () => {
    const values = [7, 7, 7, 0, 7, 7, 7];

    expect(computeRollingAverage(values)[6]).toBe(6);
  });

  it("returns an empty list for no values", () => {
    expect(computeRollingAverage([])).toEqual([]);
  });
});

describe("formatAxisDate", () => {
  it("reads the day off the string rather than through a UTC Date parse", () => {
    // `new Date("2026-01-01")` is UTC midnight and renders as Dec 31 west of Greenwich.
    expect(formatAxisDate("2026-01-01")).toBe("Jan 1");
    expect(formatAxisDate("2026-12-31")).toBe("Dec 31");
  });

  it("appends a two digit year only when asked", () => {
    expect(formatAxisDate("2025-09-07")).toBe("Sep 7");
    expect(formatAxisDate("2025-09-07", true)).toBe("Sep 7 '25");
  });

  it("passes through anything that is not a date", () => {
    expect(formatAxisDate("Sep")).toBe("Sep");
    expect(formatAxisDate("2026-13-01")).toBe("2026-13-01");
  });
});

describe("formatTooltipDate", () => {
  it("spells out the weekday and year on the local calendar day", () => {
    expect(formatTooltipDate("2026-01-01")).toBe("Thu, Jan 1, 2026");
    expect(formatTooltipDate("2026-09-07")).toBe("Mon, Sep 7, 2026");
  });

  it("passes through anything that is not a date", () => {
    expect(formatTooltipDate("W36")).toBe("W36");
  });
});
