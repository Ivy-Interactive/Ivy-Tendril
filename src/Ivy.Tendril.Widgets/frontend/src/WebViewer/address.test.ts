import { describe, it, expect } from "vitest";
import { addressParts } from "./address";

describe("addressParts", () => {
  it("drops the scheme and splits host from path", () => {
    expect(addressParts("http://localhost:5077/chat")).toEqual({ host: "localhost:5077", path: "/chat" });
  });

  it("shows only the host for the root page", () => {
    expect(addressParts("https://ivy.app/")).toEqual({ host: "ivy.app", path: "" });
    expect(addressParts("https://ivy.app")).toEqual({ host: "ivy.app", path: "" });
  });

  it("keeps the query and hash in the path", () => {
    expect(addressParts("http://localhost:5173/settings?tab=billing#top")).toEqual({
      host: "localhost:5173",
      path: "/settings?tab=billing#top",
    });
  });

  it("falls back to the raw text for anything that is not an http url", () => {
    expect(addressParts("about:blank")).toEqual({ host: "about:blank", path: "" });
    expect(addressParts("not a url")).toEqual({ host: "not a url", path: "" });
  });
});
