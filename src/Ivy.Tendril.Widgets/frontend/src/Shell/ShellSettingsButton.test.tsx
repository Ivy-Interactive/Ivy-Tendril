import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ShellSettingsButton } from "./ShellSettingsButton";

describe("ShellSettingsButton", () => {
  it("renders the label and fires OnClick", () => {
    const handler = vi.fn();
    render(<ShellSettingsButton id="settings" events={["OnClick"]} eventHandler={handler} />);

    const button = screen.getByRole("button", { name: "Settings" });
    expect(button).toHaveTextContent("Settings");
    expect(button).toHaveAttribute("data-icon-only", "false");
    expect(button.querySelector("svg.lucide-settings")).not.toBeNull();

    fireEvent.click(button);
    expect(handler).toHaveBeenCalledWith("OnClick", "settings", []);
  });

  it("is icon-only with the label as accessible name when showLabel is false", () => {
    render(
      <ShellSettingsButton
        id="inbox"
        events={[]}
        eventHandler={vi.fn()}
        label="Inbox"
        icon="Inbox"
        showLabel={false}
        isActive
      />,
    );

    const button = screen.getByRole("button", { name: "Inbox" });
    expect(button).not.toHaveTextContent("Inbox");
    expect(button).toHaveAttribute("data-icon-only", "true");
    expect(button).toHaveAttribute("data-active", "true");
    expect(button.querySelector("svg.lucide-inbox")).not.toBeNull();
  });
});
