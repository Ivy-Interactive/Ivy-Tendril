import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { ImageRenderer } from "./ImageRenderer";

describe("ImageRenderer", () => {
  it("renders an img element with correct src and alt", () => {
    render(<ImageRenderer src="https://example.com/test.jpg" alt="Test image" />);
    const img = screen.getByRole("img", { name: "Test image" });
    expect(img).toBeInTheDocument();
    expect(img).toHaveAttribute("src", "https://example.com/test.jpg");
  });

  it("shows error placeholder when image fails to load", async () => {
    render(<ImageRenderer src="https://example.com/broken.jpg" alt="Broken image" />);
    const img = screen.getByRole("img", { name: "Broken image" });

    fireEvent.error(img);

    await waitFor(() => {
      expect(screen.getByText("Broken image")).toBeInTheDocument();
      expect(screen.queryByRole("img")).not.toBeInTheDocument();
    });
  });

  it("makes image clickable after successful load", async () => {
    render(<ImageRenderer src="https://example.com/test.jpg" alt="Test image" />);
    const img = screen.getByRole("img", { name: "Test image" });

    fireEvent.load(img);

    await waitFor(() => {
      expect(img).toHaveClass("pmv-img-clickable");
    });
  });

  it("opens overlay on image click", async () => {
    render(<ImageRenderer src="https://example.com/test.jpg" alt="Test image" />);
    const img = screen.getByRole("img", { name: "Test image" });

    fireEvent.load(img);

    await waitFor(() => {
      expect(img).toHaveClass("pmv-img-clickable");
    });

    fireEvent.click(img);

    await waitFor(() => {
      const overlay = document.querySelector(".pmv-img-overlay");
      expect(overlay).toBeInTheDocument();
    });
  });

  it("closes overlay on backdrop click", async () => {
    render(<ImageRenderer src="https://example.com/test.jpg" alt="Test image" />);
    const img = screen.getByRole("img", { name: "Test image" });

    fireEvent.load(img);
    fireEvent.click(img);

    await waitFor(() => {
      const overlay = document.querySelector(".pmv-img-overlay");
      expect(overlay).toBeInTheDocument();
    });

    const overlay = document.querySelector(".pmv-img-overlay")!;
    fireEvent.click(overlay);

    await waitFor(() => {
      expect(document.querySelector(".pmv-img-overlay")).not.toBeInTheDocument();
    });
  });

  it("closes overlay on Escape key press", async () => {
    render(<ImageRenderer src="https://example.com/test.jpg" alt="Test image" />);
    const img = screen.getByRole("img", { name: "Test image" });

    fireEvent.load(img);
    fireEvent.click(img);

    await waitFor(() => {
      const overlay = document.querySelector(".pmv-img-overlay");
      expect(overlay).toBeInTheDocument();
    });

    fireEvent.keyDown(document, { key: "Escape" });

    await waitFor(() => {
      expect(document.querySelector(".pmv-img-overlay")).not.toBeInTheDocument();
    });
  });

  it("displays title in error state", async () => {
    render(
      <ImageRenderer
        src="https://example.com/broken.jpg"
        alt="Broken image"
        title="This is a title"
      />
    );
    const img = screen.getByRole("img", { name: "Broken image" });

    fireEvent.error(img);

    await waitFor(() => {
      expect(screen.getByText("This is a title")).toBeInTheDocument();
    });
  });

  it("uses alt text as fallback in error state when no alt provided", async () => {
    render(<ImageRenderer src="https://example.com/broken.jpg" />);
    const img = screen.getByAltText("");

    fireEvent.error(img);

    await waitFor(() => {
      expect(screen.getByText("Image failed to load")).toBeInTheDocument();
    });
  });
});
