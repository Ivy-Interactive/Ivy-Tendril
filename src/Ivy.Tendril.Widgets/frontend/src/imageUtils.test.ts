import { describe, it, expect, vi, beforeEach } from "vitest";
import { isImageFile, isCompressibleImage, processImageFile, MAX_IMAGE_DIMENSION } from "./imageUtils";

describe("imageUtils", () => {
  it("identifies image files correctly by mime type and extension", () => {
    expect(isImageFile("image/png")).toBe(true);
    expect(isImageFile("image/jpeg")).toBe(true);
    expect(isImageFile("photo.PNG")).toBe(true);
    expect(isImageFile("diagram.webp")).toBe(true);
    expect(isImageFile("logo.svg")).toBe(true);
    expect(isImageFile("anim.gif")).toBe(true);
    expect(isImageFile("document.pdf")).toBe(false);
    expect(isImageFile("notes.txt")).toBe(false);
  });

  it("distinguishes compressible raster images from vectors and animated gifs", () => {
    const pngFile = new File(["bytes"], "photo.png", { type: "image/png" });
    const jpegFile = new File(["bytes"], "photo.jpg", { type: "image/jpeg" });
    const svgFile = new File(["<svg></svg>"], "icon.svg", { type: "image/svg+xml" });
    const gifFile = new File(["gif"], "anim.gif", { type: "image/gif" });
    const textFile = new File(["text"], "notes.txt", { type: "text/plain" });

    expect(isCompressibleImage(pngFile)).toBe(true);
    expect(isCompressibleImage(jpegFile)).toBe(true);
    expect(isCompressibleImage(svgFile)).toBe(false);
    expect(isCompressibleImage(gifFile)).toBe(false);
    expect(isCompressibleImage(textFile)).toBe(false);
  });

  it("returns non-image files directly without processing", async () => {
    const textFile = new File(["hello world"], "hello.txt", { type: "text/plain" });
    const result = await processImageFile(textFile);
    expect(result).toBe(textFile);
  });

  it("downscales large images exceeding max dimensions using canvas", async () => {
    const origCreateObjectURL = URL.createObjectURL;
    const origRevokeObjectURL = URL.revokeObjectURL;

    URL.createObjectURL = vi.fn().mockReturnValue("blob:mock-image-url");
    URL.revokeObjectURL = vi.fn();

    const mockBlob = new Blob(["compressed-image-data"], { type: "image/webp" });

    // Mock HTMLCanvasElement
    const mockContext = {
      drawImage: vi.fn(),
    };

    const mockCanvas = {
      width: 0,
      height: 0,
      getContext: vi.fn().mockReturnValue(mockContext),
      toBlob: vi.fn((callback: (blob: Blob | null) => void) => {
        callback(mockBlob);
      }),
    };

    const origCreateElement = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tagName: string) => {
      if (tagName === "canvas") {
        return mockCanvas as any;
      }
      return origCreateElement(tagName);
    });

    // Mock Image
    const OrigImage = window.Image;
    class MockImage {
      naturalWidth = 4000;
      naturalHeight = 3000;
      width = 4000;
      height = 3000;
      onload: any = null;
      onerror: any = null;
      set src(_val: string) {
        setTimeout(() => this.onload?.(), 0);
      }
    }
    (window as any).Image = MockImage;

    try {
      const largeFile = new File(["dummy-large-bytes"], "huge_photo.png", { type: "image/png" });
      const processed = await processImageFile(largeFile, { maxDimension: 2048 });

      expect(mockCanvas.width).toBe(2048);
      expect(mockCanvas.height).toBe(1536); // (3000 * 2048) / 4000
      expect(mockContext.drawImage).toHaveBeenCalledWith(expect.anything(), 0, 0, 2048, 1536);
      expect(processed.name).toBe("huge_photo.webp");
      expect(processed.type).toBe("image/webp");
      expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mock-image-url");
    } finally {
      URL.createObjectURL = origCreateObjectURL;
      URL.revokeObjectURL = origRevokeObjectURL;
      (window as any).Image = OrigImage;
      vi.restoreAllMocks();
    }
  });
});
