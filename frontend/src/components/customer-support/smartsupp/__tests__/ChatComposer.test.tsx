import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import ChatComposer from "../ChatComposer";
import * as draftReplyHook from "../hooks/useGenerateDraftReply";
import * as sendMessageHook from "../hooks/useSendMessage";

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

const sendFn = jest.fn();
const clearSent = jest.fn();

function mockSendHook(overrides: Partial<ReturnType<typeof sendMessageHook.useSendMessage>>) {
  jest.spyOn(sendMessageHook, "useSendMessage").mockReturnValue({
    send: sendFn,
    isPending: false,
    error: null,
    justSent: false,
    clearSent,
    ...overrides,
  });
}

beforeEach(() => {
  generate.mockReset();
  reset.mockReset();
  sendFn.mockReset();
  clearSent.mockReset();
  jest.restoreAllMocks();
  mockSendHook({}); // default: idle, non-pending, no error
});

describe("ChatComposer", () => {
  it("renders an empty textarea and a disabled Send button when draft is empty", () => {
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
    screen.getAllByRole("button", { name: /generovat odpověď/i }).forEach((btn) => {
      expect(btn).toBeDisabled();
    });
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

  it("toggles the textarea height with the expand button", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.rows).toBe(5);

    fireEvent.click(screen.getByRole("button", { name: /zvětšit/i }));
    expect(textarea.rows).toBe(14);

    fireEvent.click(screen.getByRole("button", { name: /zmenšit/i }));
    expect(textarea.rows).toBe(5);
  });

  it("initializes textarea with initialDraft value", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage={null} initialDraft="Předvyplněný draft" />);
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("Předvyplněný draft");
  });

  it("calls onDraftChange when user types", () => {
    mockHook({});
    const onDraftChange = jest.fn();
    render(<ChatComposer conversationId="c1" lastContactMessage={null} onDraftChange={onDraftChange} />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), { target: { value: "Ahoj" } });
    expect(onDraftChange).toHaveBeenCalledWith("Ahoj");
  });

  it("calls onDraftChange with empty string on discard", async () => {
    const onDraftChange = jest.fn();
    mockHook({ result: { answer: "Vygenerovaná odpověď", sources: [] } });
    render(<ChatComposer conversationId="c1" lastContactMessage="Hi" onDraftChange={onDraftChange} />);
    expect(await screen.findByText("Návrh od AI")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /zahodit/i }));
    expect(onDraftChange).toHaveBeenLastCalledWith("");
  });

  it("calls onDraftChange with AI answer when draft is generated", async () => {
    const onDraftChange = jest.fn();
    mockHook({ result: { answer: "AI odpověď", sources: [] } });
    render(<ChatComposer conversationId="c1" lastContactMessage="Hi" onDraftChange={onDraftChange} />);
    await waitFor(() => expect(onDraftChange).toHaveBeenCalledWith("AI odpověď"));
  });

  it("enables Send button when draft is non-empty", () => {
    mockHook({});
    mockSendHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "Odpověď" },
    });
    expect(screen.getByRole("button", { name: /odeslat/i })).not.toBeDisabled();
  });

  it("calls send with the draft content when Send is clicked", () => {
    mockHook({});
    mockSendHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "Odpověď zákazníkovi" },
    });
    fireEvent.click(screen.getByRole("button", { name: /odeslat/i }));
    expect(sendFn).toHaveBeenCalledWith("Odpověď zákazníkovi");
  });

  it("disables Send button and shows sending state while isPending", () => {
    mockHook({});
    mockSendHook({ isPending: true });
    render(<ChatComposer conversationId="c1" lastContactMessage="Dobrý den" initialDraft="Text" />);
    const btn = screen.getByRole("button", { name: /odeslat|odesílám/i });
    expect(btn).toBeDisabled();
  });

  it("clears draft and calls onDraftChange after successful send", async () => {
    const onDraftChange = jest.fn();
    mockHook({});
    mockSendHook({ justSent: true, clearSent });
    render(
      <ChatComposer
        conversationId="c1"
        lastContactMessage="Dobrý den"
        initialDraft="Text k odeslání"
        onDraftChange={onDraftChange}
      />,
    );
    await waitFor(() => {
      const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
      expect(textarea.value).toBe("");
    });
    expect(onDraftChange).toHaveBeenLastCalledWith("");
    expect(clearSent).toHaveBeenCalled();
  });
});
