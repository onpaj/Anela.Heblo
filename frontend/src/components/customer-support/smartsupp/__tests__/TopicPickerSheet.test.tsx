import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TopicPickerSheet from "../TopicPickerSheet";

describe("TopicPickerSheet", () => {
  const onSelect = jest.fn();
  const onClose = jest.fn();

  beforeEach(() => {
    onSelect.mockClear();
    onClose.mockClear();
  });

  it("renders nothing when isOpen is false", () => {
    const { container } = render(
      <TopicPickerSheet isOpen={false} onSelect={onSelect} onClose={onClose} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it("renders all five DRAFT_REPLY_HINTS when isOpen is true", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    expect(screen.getByRole("button", { name: "Výměna zboží" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reklamace" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Doprava" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Platba" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Vrácení zboží" })).toBeInTheDocument();
  });

  it("calls onSelect with the hint label when a topic button is clicked", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: "Reklamace" }));
    expect(onSelect).toHaveBeenCalledWith("Reklamace");
    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it("calls onClose when the backdrop is clicked", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    fireEvent.click(screen.getByTestId("topic-picker-backdrop"));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("does not call onClose when the sheet panel itself is clicked", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    fireEvent.click(screen.getByRole("dialog"));
    expect(onClose).not.toHaveBeenCalled();
  });
});
