import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import ChatComposer from "../ChatComposer";

beforeEach(() => {
  localStorage.clear();
});

describe("ChatComposer", () => {
  it("renders an empty textarea and a disabled Send button", () => {
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
    expect(textarea.value).toBe("");
    const send = screen.getByRole("button", { name: /odeslat/i });
    expect(send).toBeDisabled();
  });

  it("updates draft text as the user types", () => {
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "Dobrý den" } });
    expect(textarea.value).toBe("Dobrý den");
  });

  it("inserts a knowledge-base suggestion into the textarea when its chip is clicked", () => {
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    fireEvent.click(screen.getByText("Doprava a dodací lhůty"));
    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
    expect(textarea.value).toMatch(/balíky odesíláme/);
  });

  it("appends a suggestion to existing draft text (not overwrites)", () => {
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    const textarea = screen.getByRole("textbox") as HTMLTextAreaElement;
    fireEvent.change(textarea, { target: { value: "Dobrý den, " } });
    fireEvent.click(screen.getByText("Doprava a dodací lhůty"));
    expect(textarea.value).toMatch(/Dobrý den, /);
    expect(textarea.value).toMatch(/balíky odesíláme/);
  });

  it("displays a character counter", () => {
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    fireEvent.change(screen.getByRole("textbox"), { target: { value: "hello" } });
    expect(screen.getByText(/5/)).toBeInTheDocument();
  });

  it("Send button has an explanatory title attribute", () => {
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    expect(screen.getByRole("button", { name: /odeslat/i })).toHaveAttribute(
      "title",
      expect.stringMatching(/později/i)
    );
  });
});
