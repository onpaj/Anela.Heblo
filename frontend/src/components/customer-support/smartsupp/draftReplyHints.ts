export interface DraftReplyHint {
  id: string;
  label: string;
}

/**
 * Predefined topic hints for AI draft-reply generation. The label is sent
 * verbatim to the backend as the retrieval topic.
 */
export const DRAFT_REPLY_HINTS: DraftReplyHint[] = [
  { id: "vymena", label: "Výměna zboží" },
  { id: "reklamace", label: "Reklamace" },
  { id: "doprava", label: "Doprava" },
  { id: "platba", label: "Platba" },
  { id: "vraceni", label: "Vrácení zboží" },
];
