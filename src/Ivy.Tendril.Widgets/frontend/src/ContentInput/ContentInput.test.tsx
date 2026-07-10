import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

vi.mock("pdfjs-dist", () => ({ GlobalWorkerOptions: {}, getDocument: vi.fn() }));
vi.mock("pdfjs-dist/build/pdf.worker.mjs?url", () => ({ default: "" }));

import { ContentInput } from "./ContentInput";

describe("ContentInput", () => {
  it("enables the submit button when only a file is attached (no text)", () => {
    render(<ContentInput id="civ-1" value=" [file: /tmp/foo.png]" />);
    const submitButton = screen.getByTitle("Send");
    expect(submitButton).toBeEnabled();
  });

  it("dispatches OnSubmit with a value containing the file reference when clicked", () => {
    const onIvyEvent = vi.fn();
    render(<ContentInput id="civ-1" value=" [file: /tmp/foo.png]" onIvyEvent={onIvyEvent} />);

    const submitButton = screen.getByTitle("Send");
    fireEvent.click(submitButton);

    expect(onIvyEvent).toHaveBeenCalledWith(
      "OnSubmit",
      "civ-1",
      expect.arrayContaining([
        expect.objectContaining({
          value: expect.stringContaining("[file: /tmp/foo.png]"),
        }),
      ])
    );
  });

  it("keeps the submit button disabled when there is no text and no file", () => {
    render(<ContentInput id="civ-1" value="" />);
    const submitButton = screen.getByTitle("Send");
    expect(submitButton).toBeDisabled();
  });

  it("renders the enter symbol (↵) in the shortcut label on non-Mac platforms", () => {
    render(<ContentInput id="civ-1" value="test" />);
    const shortcut = screen.getByText((_content, element) => {
      return element?.classList.contains("civ-submit-shortcut") ?? false;
    });
    expect(shortcut.textContent).toContain("↵");
  });
});
