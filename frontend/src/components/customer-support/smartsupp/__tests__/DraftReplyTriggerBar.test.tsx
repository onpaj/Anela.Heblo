import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import DraftReplyTriggerBar from "../DraftReplyTriggerBar";

describe("DraftReplyTriggerBar", () => {
  it("renders all topic hint pills and the generate button", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByText("Reklamace")).toBeInTheDocument();
    expect(screen.getByText("Výměna zboží")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeInTheDocument();
  });

  it("calls onGenerate with the hint label when a pill is clicked", () => {
    const onGenerate = jest.fn();
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={onGenerate}
      />,
    );
    fireEvent.click(screen.getByText("Reklamace"));
    expect(onGenerate).toHaveBeenCalledWith("Reklamace");
  });

  it("calls onGenerate with undefined when the generate button is clicked", () => {
    const onGenerate = jest.fn();
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={onGenerate}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /generovat odpověď/i }));
    expect(onGenerate).toHaveBeenCalledWith(undefined);
  });

  it("disables the generate button when canGenerateWithoutTopic is false", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={false}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeDisabled();
  });

  it("disables every control while disabled is true", () => {
    render(
      <DraftReplyTriggerBar
        disabled={true}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: "Reklamace" })).toBeDisabled();
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeDisabled();
  });

  it("shows an error message when error is provided", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error="AI služba je nedostupná."
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByText("AI služba je nedostupná.")).toBeInTheDocument();
  });
});
