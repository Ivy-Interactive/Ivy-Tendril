import { afterEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, waitFor } from "@testing-library/react";
import { PlanMarkdown } from "./PlanMarkdown";
import { parseWireframeFence } from "./wireframeSource";

const fence = (body: string) => "Intro\n\n```wireframe\n" + body + "\n```\n";

const stubStatus = (status: { phase: string; message?: string }) => {
  const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => status });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
};

describe("parseWireframeFence", () => {
  it("reads the mapping form", () => {
    expect(parseWireframeFence("name: checkout\nheight: 640\nviewport: Mobile")).toEqual({
      ok: true,
      spec: { name: "checkout", height: 640, viewport: "Mobile" },
    });
  });

  it("reads a bare slug as the name", () => {
    expect(parseWireframeFence("checkout-payment\n")).toEqual({ ok: true, spec: { name: "checkout-payment" } });
  });

  it.each([
    ["", "names no wireframe"],
    ["name: Not A Slug", "lowercase slug"],
    ["name: checkout\ncolour: red", "Unknown key 'colour'"],
    ["name: checkout\nheight: -4", "height"],
    ["name: checkout\nheight: 12.5", "height"],
    ["name: checkout\nviewport: desktop", "viewport"],
    ["- checkout", "Write the block as"],
  ])("rejects %j", (body, message) => {
    const result = parseWireframeFence(body);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.error).toContain(message);
  });
});

describe("wireframe fences in PlanMarkdown", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows a placeholder, never code, when the markdown is not a plan", () => {
    const { container } = render(<PlanMarkdown id="w1" content={fence("name: checkout")} />);

    expect(container.querySelector(".pmv-wireframe-placeholder")?.textContent).toContain("checkout");
    expect(container.querySelector("pre")).toBeNull();
    expect(container.querySelector("iframe")).toBeNull();
  });

  it("frames the live wireframe under the plan's address once it has built", async () => {
    const fetchMock = stubStatus({ phase: "running" });

    const { container } = render(
      <PlanMarkdown
        id="w1"
        content={fence("name: checkout")}
        wireframeBaseUrl="/__wireframes/42/"
      />,
    );

    await waitFor(() => expect(container.querySelector("iframe")).not.toBeNull());

    expect(fetchMock).toHaveBeenCalledWith("/__wireframes/42/checkout/__wireframe/status", expect.anything());
    expect(container.querySelector("iframe")?.getAttribute("src")).toBe("/__wireframes/42/checkout/");
    expect(container.querySelector("pre")).toBeNull();
    expect(container.querySelector("img")).toBeNull();

    // An icon, labelled for the tooltip and for assistive technology.
    expect(container.querySelector('button[aria-label="Open Full Size"]')).not.toBeNull();
  });

  it("opens the wireframe full size in an overlay inside the page, never a new window", async () => {
    // A new window does nothing in the desktop window, and an external browser does not trust the
    // desktop app's certificate, so full size stays in the page.
    stubStatus({ phase: "running" });
    const eventHandler = vi.fn();
    const windowOpen = vi.fn();
    vi.stubGlobal("open", windowOpen);

    const { container } = render(
      <PlanMarkdown
        id="w1"
        content={fence("name: checkout")}
        wireframeBaseUrl="/__wireframes/42/"
        events={["OnLinkClick"]}
        eventHandler={eventHandler}
      />,
    );

    await waitFor(() => expect(container.querySelector('button[aria-label="Open Full Size"]')).not.toBeNull());
    fireEvent.click(container.querySelector('button[aria-label="Open Full Size"]')!);

    const overlay = document.body.querySelector('[role="dialog"].pmv-wireframe-overlay');
    expect(overlay).not.toBeNull();
    expect(overlay?.getAttribute("aria-label")).toBe("checkout");
    expect(overlay?.querySelector("iframe")?.getAttribute("src")).toBe("/__wireframes/42/checkout/");
    expect(windowOpen).not.toHaveBeenCalled();
    expect(eventHandler).not.toHaveBeenCalled();

    fireEvent.keyDown(document, { key: "Escape" });
    await waitFor(() => expect(document.body.querySelector(".pmv-wireframe-overlay")).toBeNull());
  });

  it("closes the full-size overlay from its close button", async () => {
    stubStatus({ phase: "running" });

    const { container } = render(
      <PlanMarkdown id="w1" content={fence("name: checkout")} wireframeBaseUrl="/__wireframes/42/" />,
    );

    await waitFor(() => expect(container.querySelector('button[aria-label="Open Full Size"]')).not.toBeNull());
    fireEvent.click(container.querySelector('button[aria-label="Open Full Size"]')!);
    fireEvent.click(document.body.querySelector('.pmv-wireframe-overlay button[aria-label="Close"]')!);

    await waitFor(() => expect(document.body.querySelector(".pmv-wireframe-overlay")).toBeNull());
  });

  it("ignores a caption left in a plan written before captions were removed, and shows no title", async () => {
    stubStatus({ phase: "running" });

    const { container } = render(
      <PlanMarkdown id="w1" content={fence("name: checkout\ncaption: Converter Page")} wireframeBaseUrl="/__wireframes/42/" />,
    );

    await waitFor(() => expect(container.querySelector("iframe")).not.toBeNull());
    expect(container.querySelector(".pmv-wireframe-invalid")).toBeNull();
    expect(container.textContent).not.toContain("Converter Page");
  });

  it("says when the plan has no wireframe by that name", async () => {
    stubStatus({ phase: "missing" });

    const { container } = render(
      <PlanMarkdown id="w1" content={fence("name: nope")} wireframeBaseUrl="/__wireframes/42/" />,
    );

    await waitFor(() => expect(container.textContent).toContain("no wireframe named"));
    expect(container.querySelector("iframe")).toBeNull();
  });

  it("reports a build failure in words, without the build output", async () => {
    stubStatus({ phase: "failed", message: "The latest version of this wireframe does not build." });

    const { container } = render(
      <PlanMarkdown id="w1" content={fence("name: checkout")} wireframeBaseUrl="/__wireframes/42/" />,
    );

    await waitFor(() => expect(container.textContent).toContain("does not build"));
    expect(container.querySelector("pre")).toBeNull();
    expect(container.querySelector("iframe")).toBeNull();
  });

  it("explains an invalid block instead of rendering it as code", () => {
    const { container } = render(
      <PlanMarkdown id="w1" content={fence("name: Not A Slug")} wireframeBaseUrl="/__wireframes/42/" />,
    );

    expect(container.querySelector(".pmv-wireframe-invalid")?.textContent).toContain("not valid");
    expect(container.querySelector("pre")).toBeNull();
  });

  it("sizes the frame from its own page and ignores messages from anywhere else", async () => {
    stubStatus({ phase: "running" });

    const { container } = render(
      <PlanMarkdown id="w1" content={fence("name: checkout")} wireframeBaseUrl="/__wireframes/42/" />,
    );

    await waitFor(() => expect(container.querySelector("iframe")).not.toBeNull());
    const frame = container.querySelector("iframe")!;
    const initial = frame.style.height;

    const post = (source: MessageEventSource | null) =>
      act(() => {
        window.dispatchEvent(
          new MessageEvent("message", {
            data: { source: "tendril-wireframe", type: "size", height: 700 },
            origin: window.location.origin,
            source,
          }),
        );
      });

    post(window);
    expect(frame.style.height).toBe(initial);

    post(frame.contentWindow);
    expect(frame.style.height).toBe("700px");
  });

  describe("sizing in a 720 wide column", () => {
    const stubColumn = (width: number) =>
      vi.stubGlobal(
        "ResizeObserver",
        class {
          constructor(private readonly callback: (entries: { contentRect: { width: number; height: number } }[]) => void) {}
          observe() {
            this.callback([{ contentRect: { width, height: 0 } }]);
          }
          disconnect() {}
        },
      );

    const renderSized = async (size: { width: number; height: number }) => {
      stubStatus({ phase: "running" });
      stubColumn(720);

      const { container } = render(
        <PlanMarkdown id="w1" content={fence("name: checkout")} wireframeBaseUrl="/__wireframes/42/" />,
      );

      await waitFor(() => expect(container.querySelector("iframe")).not.toBeNull());
      const frame = container.querySelector("iframe")!;

      act(() => {
        window.dispatchEvent(
          new MessageEvent("message", {
            data: { source: "tendril-wireframe", type: "size", ...size },
            origin: window.location.origin,
            source: frame.contentWindow,
          }),
        );
      });

      return { frame, scaled: container.querySelector<HTMLElement>(".pmv-wireframe-frame-scaled")! };
    };

    it("does not zoom a page that fits the column", async () => {
      const { frame } = await renderSized({ width: 720, height: 500 });

      expect(frame.style.width).toBe("100%");
      expect(frame.style.transform).toBe("");
      expect(frame.style.height).toBe("500px");
    });

    it("zooms out on width only when the content is wider than the column", async () => {
      const { frame, scaled } = await renderSized({ width: 1440, height: 600 });

      expect(frame.style.width).toBe("1440px");
      expect(frame.style.transform).toBe("scale(0.5)");
      expect(scaled.style.width).toBe("720px");
      expect(scaled.style.height).toBe("300px");
    });

    it("caps the height and lets a long page scroll instead of zooming it", async () => {
      const { frame, scaled } = await renderSized({ width: 720, height: 3000 });

      expect(frame.style.transform).toBe("");
      expect(scaled.style.height).toBe("800px");
      expect(frame.style.height).toBe("800px");
    });
  });
});
