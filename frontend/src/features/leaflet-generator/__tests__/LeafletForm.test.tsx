import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import LeafletForm from "../LeafletForm";
import { AudienceType, LeafletLength } from "../../../api/generated/api-client";

const defaultProps = {
  topic: "",
  audience: AudienceType.EndConsumer,
  length: LeafletLength.Short,
  isLoading: false,
  onTopicChange: jest.fn(),
  onAudienceChange: jest.fn(),
  onLengthChange: jest.fn(),
  onSubmit: jest.fn(),
};

function renderForm(overrides: Partial<typeof defaultProps> = {}) {
  return render(<LeafletForm {...defaultProps} {...overrides} />);
}

describe("LeafletForm", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("renders all Czech labels", () => {
    renderForm();

    expect(screen.getByText("Téma")).toBeInTheDocument();
    expect(screen.getByText("Cílová skupina")).toBeInTheDocument();
    expect(screen.getByText("Délka")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Vygenerovat leták" })
    ).toBeInTheDocument();
  });

  it("disables submit when topic is empty", () => {
    renderForm({ topic: "" });

    const button = screen.getByRole("button", { name: "Vygenerovat leták" });
    expect(button).toBeDisabled();
  });

  it("disables submit when isLoading is true", () => {
    renderForm({ topic: "x", isLoading: true });

    const button = screen.getByRole("button", { name: "Vygenerovat leták" });
    expect(button).toBeDisabled();
  });

  it("calls onTopicChange when input changes", async () => {
    const onTopicChange = jest.fn();
    userEvent.setup();

    renderForm({ onTopicChange });

    const input = screen.getByRole("textbox", { name: "Téma" });
    await userEvent.type(input, "a");

    expect(onTopicChange).toHaveBeenCalledWith("a");
  });

  it("enforces 200-character maxLength attribute", () => {
    renderForm();

    const input = screen.getByRole("textbox", { name: "Téma" });
    expect(input).toHaveAttribute("maxLength", "200");
  });

  it("calls onSubmit when form is submitted with valid topic", async () => {
    const onSubmit = jest.fn();
    const user = userEvent.setup();

    renderForm({ topic: "Bisabolol pro citlivou pleť", onSubmit });

    const button = screen.getByRole("button", { name: "Vygenerovat leták" });
    await user.click(button);

    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it("does not call onSubmit when topic is empty", async () => {
    const onSubmit = jest.fn();
    userEvent.setup();

    renderForm({ topic: "", onSubmit });

    const button = screen.getByRole("button", { name: "Vygenerovat leták" });
    const form = button.closest("form");
    if (form) {
      fireEvent.submit(form);
    }

    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("calls onAudienceChange when B2B radio is clicked", async () => {
    const onAudienceChange = jest.fn();
    const user = userEvent.setup();

    renderForm({ onAudienceChange });

    const b2bRadio = screen.getByRole("radio", { name: "B2B" });
    await user.click(b2bRadio);

    expect(onAudienceChange).toHaveBeenCalledWith(AudienceType.B2B);
  });

  it("calls onLengthChange when Dlouhý radio is clicked", async () => {
    const onLengthChange = jest.fn();
    const user = userEvent.setup();

    renderForm({ onLengthChange });

    const longRadio = screen.getByRole("radio", { name: "Dlouhý (~700 slov)" });
    await user.click(longRadio);

    expect(onLengthChange).toHaveBeenCalledWith(LeafletLength.Long);
  });

  it("does not call onSubmit when topic is whitespace-only", async () => {
    renderForm({ topic: "   " });

    const button = screen.getByRole("button", { name: "Vygenerovat leták" });
    expect(button).toBeDisabled();
  });
});
