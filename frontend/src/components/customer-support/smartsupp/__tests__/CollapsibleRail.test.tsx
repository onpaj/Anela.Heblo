import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import CollapsibleRail from "../CollapsibleRail";

describe("CollapsibleRail", () => {
  it("calls onToggle when the rail button is clicked", () => {
    // Arrange
    const onToggle = jest.fn();
    render(
      <CollapsibleRail side="left" isOpen label="Seznam konverzací" onToggle={onToggle} />,
    );

    // Act
    fireEvent.click(screen.getByRole("button"));

    // Assert
    expect(onToggle).toHaveBeenCalledTimes(1);
  });

  it("labels the action 'Sbalit' when the panel is open", () => {
    render(
      <CollapsibleRail side="left" isOpen label="Seznam konverzací" onToggle={() => {}} />,
    );

    expect(
      screen.getByRole("button", { name: "Sbalit Seznam konverzací" }),
    ).toBeInTheDocument();
  });

  it("labels the action 'Rozbalit' when the panel is collapsed", () => {
    render(
      <CollapsibleRail
        side="right"
        isOpen={false}
        label="Detail kontaktu"
        onToggle={() => {}}
      />,
    );

    expect(
      screen.getByRole("button", { name: "Rozbalit Detail kontaktu" }),
    ).toBeInTheDocument();
  });

  it("exposes a side-specific test id", () => {
    render(
      <CollapsibleRail side="right" isOpen label="Detail kontaktu" onToggle={() => {}} />,
    );

    expect(screen.getByTestId("collapsible-rail-right")).toBeInTheDocument();
  });
});
