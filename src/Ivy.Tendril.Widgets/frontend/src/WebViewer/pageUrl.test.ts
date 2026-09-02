import { describe, expect, it } from "vitest";
import { canonicalPageUrl } from "./pageUrl";

describe("canonicalPageUrl", () => {
  it("drops the hash, so an anchor link does not become another page", () => {
    expect(canonicalPageUrl("http://localhost:5174/tool/html-encoder#output")).toBe(
      "http://localhost:5174/tool/html-encoder",
    );
    expect(canonicalPageUrl("http://localhost:5174/#top")).toBe("http://localhost:5174/");
  });

  it("treats a trailing slash as the same page", () => {
    expect(canonicalPageUrl("http://localhost:5174/tool/html-encoder/")).toBe(
      canonicalPageUrl("http://localhost:5174/tool/html-encoder"),
    );
  });

  it("keeps the query, which in a single-page app is usually the route", () => {
    expect(canonicalPageUrl("http://localhost:5174/settings?tab=billing")).not.toBe(
      canonicalPageUrl("http://localhost:5174/settings?tab=profile"),
    );
  });

  it("normalizes host case and a default port away", () => {
    expect(canonicalPageUrl("http://LocalHost:80/a")).toBe(canonicalPageUrl("http://localhost/a"));
    expect(canonicalPageUrl("https://Example.COM:443/a")).toBe("https://example.com/a");
  });

  it("keeps path case, because servers do", () => {
    expect(canonicalPageUrl("http://localhost:5174/Tool")).not.toBe(
      canonicalPageUrl("http://localhost:5174/tool"),
    );
  });

  it("separates pages that differ only by port", () => {
    expect(canonicalPageUrl("http://localhost:5174/a")).not.toBe(
      canonicalPageUrl("http://localhost:5173/a"),
    );
  });

  it("falls back to the raw string when there is nothing to parse", () => {
    expect(canonicalPageUrl("about:blank")).toBe("about:blank");
    expect(canonicalPageUrl("")).toBe("");
  });
});
