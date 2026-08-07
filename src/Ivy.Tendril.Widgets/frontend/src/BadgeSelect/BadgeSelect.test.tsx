import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";

import { BadgeSelect, type BadgeSelectOption } from "./BadgeSelect";

const projectOptions: BadgeSelectOption[] = [
  { value: "Tendril-Services", label: "Tendril-Services" },
  { value: "lots-of-dev-tools", label: "lots-of-dev-tools" },
];

const addProjectAction: BadgeSelectOption[] = [
  { value: "__tendril_add_project__", label: "Add Project", icon: "Plus" },
];

describe("BadgeSelect actions", () => {
  it("renders the action row label below the options when the trigger is opened", () => {
    render(<BadgeSelect id="bs-1" options={projectOptions} actions={addProjectAction} />);

    fireEvent.click(screen.getByRole("button", { name: /select/i }));

    const optionLabels = screen.getAllByRole("option").map((el) => el.textContent);
    const actionButton = screen.getByText("Add Project");

    expect(optionLabels).toEqual(["Tendril-Services", "lots-of-dev-tools"]);
    expect(actionButton).toBeInTheDocument();
    expect(actionButton).not.toHaveAttribute("role", "option");
  });

  it("emits OnAction with the action value and not OnChange when the action row is clicked", () => {
    const eventHandler = vi.fn();
    render(
      <BadgeSelect
        id="bs-1"
        options={projectOptions}
        actions={addProjectAction}
        events={["OnChange", "OnAction"]}
        eventHandler={eventHandler}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /select/i }));
    fireEvent.click(screen.getByText("Add Project"));

    expect(eventHandler).toHaveBeenCalledWith("OnAction", "bs-1", ["__tendril_add_project__"]);
    expect(eventHandler).not.toHaveBeenCalledWith("OnChange", "bs-1", expect.anything());
  });

  it("closes the menu when the action row is clicked", () => {
    render(
      <BadgeSelect
        id="bs-1"
        options={projectOptions}
        actions={addProjectAction}
        events={["OnAction"]}
        eventHandler={vi.fn()}
      />,
    );

    const trigger = screen.getByRole("button", { name: /select/i });
    fireEvent.click(trigger);
    fireEvent.click(screen.getByText("Add Project"));

    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("still emits OnChange for a normal option and never includes the action value", () => {
    const eventHandler = vi.fn();
    render(
      <BadgeSelect
        id="bs-1"
        options={projectOptions}
        actions={addProjectAction}
        multiple
        events={["OnChange", "OnAction"]}
        eventHandler={eventHandler}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /select/i }));
    fireEvent.click(screen.getByText("Tendril-Services"));

    expect(eventHandler).toHaveBeenCalledWith("OnChange", "bs-1", [["Tendril-Services"]]);
    const [, , args] = eventHandler.mock.calls[0];
    expect((args as string[][])[0]).not.toContain("__tendril_add_project__");
  });

  it("renders no separator and no extra row when actions is omitted", () => {
    render(<BadgeSelect id="bs-1" options={projectOptions} />);

    fireEvent.click(screen.getByRole("button", { name: /select/i }));

    expect(document.querySelector(".bselect-separator")).toBeNull();
    expect(document.querySelectorAll(".bselect-action").length).toBe(0);
  });
});
