import { renderHook } from "@testing-library/react";
import { useKnowledgeBaseSuggestions } from "../useKnowledgeBaseSuggestions";

describe("useKnowledgeBaseSuggestions", () => {
  it("returns a non-empty list of mock suggestions when given a conversation id", () => {
    const { result } = renderHook(() =>
      useKnowledgeBaseSuggestions("c1", "Mám dotaz na dopravu")
    );
    expect(result.current.suggestions.length).toBeGreaterThan(0);
  });

  it("returns suggestions with title + content", () => {
    const { result } = renderHook(() =>
      useKnowledgeBaseSuggestions("c1", "Dotaz")
    );
    const first = result.current.suggestions[0];
    expect(typeof first.title).toBe("string");
    expect(typeof first.content).toBe("string");
    expect(first.title.length).toBeGreaterThan(0);
    expect(first.content.length).toBeGreaterThan(0);
  });

  it("returns isLoading=false for the mock implementation", () => {
    const { result } = renderHook(() => useKnowledgeBaseSuggestions("c1", null));
    expect(result.current.isLoading).toBe(false);
  });

  it("returns an empty list when conversationId is null", () => {
    const { result } = renderHook(() => useKnowledgeBaseSuggestions(null, null));
    expect(result.current.suggestions).toEqual([]);
  });
});
