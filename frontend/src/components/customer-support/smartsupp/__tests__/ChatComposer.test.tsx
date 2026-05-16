import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ChatComposer from "../ChatComposer";
import * as draftReplyHook from "../hooks/useGenerateDraftReply";

const generate = jest.fn();
const reset = jest.fn();

function mockHook(overrides: Partial<ReturnType<typeof draftReplyHook.useGenerateDraftReply>>) {
  jest.spyOn(draftReplyHook, "useGenerateDraftReply").mockReturnValue({
    generate,
    isLoading: false,
    error: null,
    result: null,
    reset,
    ...overrides,
  });
}

beforeEach(() => {
  generate.mockReset();
  reset.mockReset();
  jest.restoreAllMocks();
});

describe("ChatComposer", () => {
  it("renders an empty textarea and a disabled Send button", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("");
    expect(screen.getByRole("button", { name: /odeslat/i })).toBeDisabled();
  });

  it("calls generate with the hint label when a topic pill is clicked", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.click(screen.getByText("Reklamace"));
    expect(generate).toHaveBeenCalledWith("Reklamace");
  });

  it("disables the generate button when there is no contact message", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage={null} />);
    expect(screen.getByRole("button", { name: /generovat odpověď/i })).toBeDisabled();
  });

  it("places the generated answer into the textarea and shows the AI toolbar", async () => {
    mockHook({
      result: {
        answer: "Dobrý den, reklamaci vyřídíme do 14 dnů.",
        sources: [{ documentId: "d1", filename: "reklamace.pdf", excerpt: "...", score: 0.9 }],
      },
    });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    await waitFor(() => {
      const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
      expect(textarea.value).toMatch(/reklamaci vyřídíme/);
    });
    expect(screen.getByText("Návrh od AI")).toBeInTheDocument();
  });

  it("clears the draft and hides the toolbar on discard", async () => {
    mockHook({
      result: { answer: "Vygenerovaná odpověď", sources: [] },
    });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    expect(await screen.findByText("Návrh od AI")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /zahodit/i }));
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("");
    expect(screen.queryByText("Návrh od AI")).not.toBeInTheDocument();
  });

  it("hides the AI toolbar once the agent edits the generated draft", async () => {
    mockHook({
      result: { answer: "Vygenerovaná odpověď", sources: [] },
    });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    expect(await screen.findByText("Návrh od AI")).toBeInTheDocument();
    const textarea = screen.getByPlaceholderText(/napište odpověď/i);
    fireEvent.change(textarea, { target: { value: "Ručně upravený text" } });
    expect(screen.queryByText("Návrh od AI")).not.toBeInTheDocument();
  });

  it("shows the hook error in the trigger bar", () => {
    mockHook({ error: "AI služba je nedostupná." });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    expect(screen.getByText("AI služba je nedostupná.")).toBeInTheDocument();
  });

  it("displays a character counter", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "hello" },
    });
    expect(screen.getByText(/5 \//)).toBeInTheDocument();
  });
});
