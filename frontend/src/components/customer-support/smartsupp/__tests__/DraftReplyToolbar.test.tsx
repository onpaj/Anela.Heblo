import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import DraftReplyToolbar from "../DraftReplyToolbar";
import { DraftReplySource } from "../hooks/useGenerateDraftReply";

const sources: DraftReplySource[] = [
  { chunkId: 'chunk-1', documentId: 'd1', filename: 'reklamace.pdf', excerpt: '...', score: 0.9 },
];

describe("DraftReplyToolbar", () => {
  it("calls onRegenerate when the regenerate button is clicked", () => {
    const onRegenerate = jest.fn();
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={onRegenerate} onDiscard={jest.fn()} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /regenerovat/i }));
    expect(onRegenerate).toHaveBeenCalledTimes(1);
  });

  it("calls onDiscard when the discard button is clicked", () => {
    const onDiscard = jest.fn();
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={jest.fn()} onDiscard={onDiscard} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /zahodit/i }));
    expect(onDiscard).toHaveBeenCalledTimes(1);
  });

  it("reveals source filenames in a tooltip on hover", () => {
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={jest.fn()} onDiscard={jest.fn()} />,
    );
    fireEvent.mouseEnter(screen.getByRole("button", { name: /zdroje/i }));
    expect(screen.getByRole("tooltip")).toHaveTextContent("reklamace.pdf");
  });

  it("does not render the sources control when there are no sources", () => {
    render(
      <DraftReplyToolbar sources={[]} onRegenerate={jest.fn()} onDiscard={jest.fn()} />,
    );
    expect(screen.queryByRole("button", { name: /zdroje/i })).not.toBeInTheDocument();
  });
});
