export interface KnowledgeBaseSuggestion {
  id: string;
  title: string;
  content: string;
}

interface UseKnowledgeBaseSuggestionsResult {
  suggestions: KnowledgeBaseSuggestion[];
  isLoading: boolean;
}

const MOCK_SUGGESTIONS: KnowledgeBaseSuggestion[] = [
  {
    id: "sug-doprava",
    title: "Doprava a dodací lhůty",
    content:
      "Dobrý den, balíky odesíláme do 24 hodin po obdržení platby. Doručení Zásilkovnou trvá obvykle 1–2 pracovní dny.",
  },
  {
    id: "sug-reklamace",
    title: "Reklamace",
    content:
      "Dobrý den, reklamaci můžete uplatnit do 14 dnů od převzetí zboží. Stačí nám napsat na info@anela.cz se snímkem produktu.",
  },
  {
    id: "sug-vraceni",
    title: "Vrácení zboží",
    content:
      "Dobrý den, na vrácení zboží máte 14 dní od převzetí. Pošlete nám prosím nepoužité zboží zpět spolu s číslem objednávky.",
  },
  {
    id: "sug-platba",
    title: "Platební metody",
    content:
      "Dobrý den, přijímáme platbu kartou, převodem, dobírkou i přes Google/Apple Pay. Vše šifrované přes Comgate.",
  },
];

// TODO: wire to /api/knowledge-base/ask once the conversation-context endpoint is ready.
// Signature is intentionally identical to the future server-backed hook.
export function useKnowledgeBaseSuggestions(
  conversationId: string | null,
  _lastContactMessage: string | null,
): UseKnowledgeBaseSuggestionsResult {
  if (!conversationId) {
    return { suggestions: [], isLoading: false };
  }
  return { suggestions: MOCK_SUGGESTIONS, isLoading: false };
}
