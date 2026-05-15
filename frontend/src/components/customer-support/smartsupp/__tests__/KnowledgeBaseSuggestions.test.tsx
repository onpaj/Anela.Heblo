import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import KnowledgeBaseSuggestions from "../KnowledgeBaseSuggestions";

beforeEach(() => {
  localStorage.clear();
});

describe("KnowledgeBaseSuggestions", () => {
  it("renders suggestion chips by title", () => {
    render(
      <KnowledgeBaseSuggestions conversationId="c1" lastContactMessage={null} onSelect={() => {}} />
    );
    expect(screen.getByText("Doprava a dodací lhůty")).toBeInTheDocument();
    expect(screen.getByText("Reklamace")).toBeInTheDocument();
  });

  it("invokes onSelect with the suggestion content when a chip is clicked", () => {
    const onSelect = jest.fn();
    render(
      <KnowledgeBaseSuggestions conversationId="c1" lastContactMessage={null} onSelect={onSelect} />
    );
    fireEvent.click(screen.getByText("Doprava a dodací lhůty"));
    expect(onSelect).toHaveBeenCalledWith(expect.stringContaining("balíky odesíláme"));
  });

  it("collapses when the toggle is clicked and persists state in localStorage", () => {
    render(
      <KnowledgeBaseSuggestions conversationId="c1" lastContactMessage={null} onSelect={() => {}} />
    );
    fireEvent.click(screen.getByTestId("kb-toggle"));
    expect(screen.queryByText("Doprava a dodací lhůty")).not.toBeInTheDocument();
    expect(localStorage.getItem("smartsupp.kbSuggestions.collapsed")).toBe("true");
  });

  it("starts collapsed when localStorage has the collapsed flag", () => {
    localStorage.setItem("smartsupp.kbSuggestions.collapsed", "true");
    render(
      <KnowledgeBaseSuggestions conversationId="c1" lastContactMessage={null} onSelect={() => {}} />
    );
    expect(screen.queryByText("Doprava a dodací lhůty")).not.toBeInTheDocument();
  });

  it("renders nothing when conversationId is null", () => {
    const { container } = render(
      <KnowledgeBaseSuggestions conversationId={null} lastContactMessage={null} onSelect={() => {}} />
    );
    expect(container).toBeEmptyDOMElement();
  });
});
