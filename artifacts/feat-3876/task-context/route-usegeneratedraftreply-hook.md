### task: route-usegeneratedraftreply-hook

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts`

#### Step 1: Rewrite `useGenerateDraftReply.ts`

Replace the entire file with:

```ts
import { useMutation } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../../../../api/client";
import {
  ErrorCodes,
  GenerateDraftReplyBody,
  type GenerateDraftReplyResponse,
} from "../../../../api/generated/api-client";

export interface DraftReplySource {
  chunkId: string;
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface DraftReplyResult {
  id: string | null;
  answer: string;
  sources: DraftReplySource[];
}

const ERROR_MESSAGES: Partial<Record<ErrorCodes, string>> = {
  [ErrorCodes.SmartsuppDraftReplyAiUnavailable]:
    "AI služba je momentálně nedostupná. Zkuste to prosím znovu.",
  [ErrorCodes.SmartsuppConversationEmpty]: "Konverzace neobsahuje zprávu zákazníka.",
  [ErrorCodes.SmartsuppConversationNotFound]: "Konverzace nebyla nalezena.",
};

function messageForError(code?: ErrorCodes): string {
  if (code && ERROR_MESSAGES[code]) {
    return ERROR_MESSAGES[code]!;
  }
  return "Nepodařilo se vygenerovat odpověď.";
}

function toDraftReplyResult(data: GenerateDraftReplyResponse): DraftReplyResult {
  return {
    id: data.id ?? null,
    answer: data.answer ?? "",
    sources: (data.sources ?? []).map((s) => ({
      chunkId: s.chunkId ?? "",
      documentId: s.documentId ?? "",
      filename: s.filename ?? "",
      excerpt: s.excerpt ?? "",
      score: s.score ?? 0,
    })),
  };
}

interface UseGenerateDraftReplyResult {
  generate: (topic?: string) => void;
  isLoading: boolean;
  error: string | null;
  result: DraftReplyResult | null;
  reset: () => void;
}

export function useGenerateDraftReply(
  conversationId: string | null,
): UseGenerateDraftReplyResult {
  const mutation = useMutation<DraftReplyResult, Error, string | undefined>({
    mutationFn: async (topic) => {
      if (!conversationId) {
        throw new Error("Není vybrána konverzace.");
      }

      let data: GenerateDraftReplyResponse;
      try {
        data = await getAuthenticatedApiClient().smartsupp_GenerateDraftReply(
          conversationId,
          new GenerateDraftReplyBody({ topic: topic ?? undefined }),
        );
      } catch {
        // 400/404/503 are all untyped ProducesResponseType on this controller action, so the
        // generated client throws without a usable errorCode here.
        throw new Error(messageForError(undefined));
      }
      if (!data.success) {
        throw new Error(messageForError(data.errorCode));
      }

      return toDraftReplyResult(data);
    },
  });

  return {
    generate: (topic?: string) => mutation.mutate(topic),
    isLoading: mutation.isPending,
    error: mutation.error ? mutation.error.message : null,
    result: mutation.data ?? null,
    reset: mutation.reset,
  };
}
```

Note: `topic ?? undefined` (not `?? null`) because the generated `GenerateDraftReplyBody.topic` is typed `string | undefined`, not `string | null`; omitting the key from the outgoing JSON has the same effect server-side as sending an explicit `null` did before, since the backend's `Topic` property is a nullable C# string either way.

#### Step 2: Rewrite `useGenerateDraftReply.test.ts`

Replace `frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts` with:

```ts
import { renderHook, act, waitFor } from "@testing-library/react";
import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useGenerateDraftReply } from "../useGenerateDraftReply";
import { getAuthenticatedApiClient } from "../../../../../api/client";

jest.mock("../../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockGenerateDraftReply = jest.fn();

function wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return React.createElement(QueryClientProvider, { client }, children);
}

beforeEach(() => {
  mockGenerateDraftReply.mockReset();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    smartsupp_GenerateDraftReply: mockGenerateDraftReply,
  });
});

describe("useGenerateDraftReply", () => {
  it("returns answer and sources on success", async () => {
    mockGenerateDraftReply.mockResolvedValue({
      success: true,
      answer: "Dobrý den, balíky odesíláme do 24 hodin.",
      sources: [{ documentId: "d1", filename: "doprava.pdf", excerpt: "...", score: 0.9 }],
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Doprava"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(result.current.result!.answer).toMatch(/balíky odesíláme/);
    expect(result.current.result!.sources).toHaveLength(1);
  });

  it("passes the topic through the typed client", async () => {
    mockGenerateDraftReply.mockResolvedValue({ success: true, answer: "x", sources: [] });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate("Reklamace"));

    await waitFor(() => expect(result.current.result).not.toBeNull());
    expect(mockGenerateDraftReply).toHaveBeenCalledWith(
      "c1",
      expect.objectContaining({ topic: "Reklamace" }),
    );
  });

  it("surfaces a Czech message for a known error code on the typed response", async () => {
    mockGenerateDraftReply.mockResolvedValue({
      success: false,
      errorCode: "SmartsuppDraftReplyAiUnavailable",
    });

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/nedostupná/i);
  });

  it("surfaces a generic message when the call throws (untyped 400/404/503)", async () => {
    mockGenerateDraftReply.mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useGenerateDraftReply("c1"), { wrapper });
    act(() => result.current.generate(undefined));

    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.error).toMatch(/Nepodařilo se/i);
  });
});
```

#### Step 3: Run the test and full type-check

```bash
cd frontend
CI=true npx react-scripts test src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts --watchAll=false
npm run build
```

Both should be clean.

#### Step 4: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts \
  frontend/src/components/customer-support/smartsupp/hooks/__tests__/useGenerateDraftReply.test.ts
git commit -m "Route useGenerateDraftReply through the generated typed API client"
```

---
