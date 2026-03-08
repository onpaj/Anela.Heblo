# Knowledge Base Missing Features — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add 5 missing features to the Knowledge Base RAG system: Frontend UI, Document Delete, Additional Document Formats (.docx, .txt), Retry Logic for external APIs, and Integration Tests.

**Architecture:** Backend follows Clean Architecture with MediatR (handler per use case). Frontend uses React + TanStack Query with absolute-URL hooks. All new backend tasks follow the existing pattern: `Domain → Application → Persistence → API`. Bug fixes plan (`2026-03-07-knowledge-base-bug-fixes.md`) MUST be completed first — it creates `GetDocumentsHandler`, `DeleteDocumentAsync` in the repository interface/implementation, and fixes the controller.

**Tech Stack:** .NET 8, MediatR, EF Core + pgvector, React 18, TypeScript, TanStack Query, Jest, Polly, DocumentFormat.OpenXml, Testcontainers

---

## Prerequisites

- Bug fixes plan `docs/plans/2026-03-07-knowledge-base-bug-fixes.md` fully complete
- `IKnowledgeBaseRepository` has `DeleteDocumentAsync` and `GetDocumentBySourcePathAsync`
- `GetDocumentsHandler` exists and controller uses MediatR for all actions
- `dotnet build` and `dotnet test` pass cleanly

---

## Feature 1: Frontend UI

### Task 1.1: Regenerate OpenAPI TypeScript client

**Files:**
- Run build + client generation

**Step 1: Build backend to update OpenAPI spec**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

**Step 2: Regenerate TypeScript client**

```bash
cd frontend && npm run generate-api-client
```

**Step 3: Verify generated methods exist**

Open `frontend/src/api/generated/api-client.ts` and confirm these three methods exist:
- `knowledgeBase_GetDocuments()`
- `knowledgeBase_Search(body: SearchDocumentsRequest)`
- `knowledgeBase_Ask(body: AskQuestionRequest)`

**Step 4: Commit**

```bash
git add frontend/src/api/generated/
git commit -m "chore: regenerate OpenAPI client with KnowledgeBase endpoints"
```

---

### Task 1.2: Create API hooks

**Files:**
- Create: `frontend/src/api/hooks/useKnowledgeBase.ts`
- Create: `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`

**Step 1: Write the failing test**

```typescript
// frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts
/**
 * @jest-environment jsdom
 */
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import React from 'react';
import {
  useKnowledgeBaseDocumentsQuery,
  useKnowledgeBaseSearchMutation,
  useKnowledgeBaseAskMutation,
  useDeleteKnowledgeBaseDocumentMutation,
} from '../useKnowledgeBase';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

const mockFetch = jest.fn();
const mockApiClient = {
  baseUrl: 'http://localhost:5001',
  http: { fetch: mockFetch },
};

const { getAuthenticatedApiClient } = require('../../client');

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('useKnowledgeBase hooks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    getAuthenticatedApiClient.mockResolvedValue(mockApiClient);
  });

  describe('useKnowledgeBaseDocumentsQuery', () => {
    it('fetches documents and returns array', async () => {
      const mockDocs = [
        { id: 'doc-1', filename: 'test.pdf', status: 'indexed', contentType: 'application/pdf', createdAt: '2026-01-01T00:00:00Z' },
      ];
      mockFetch.mockResolvedValueOnce({ ok: true, json: async () => mockDocs });

      const { result } = renderHook(() => useKnowledgeBaseDocumentsQuery(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockDocs);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/documents',
        { method: 'GET' }
      );
    });

    it('throws on non-ok response', async () => {
      mockFetch.mockResolvedValueOnce({ ok: false, status: 500 });
      const { result } = renderHook(() => useKnowledgeBaseDocumentsQuery(), {
        wrapper: createWrapper(),
      });
      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  describe('useKnowledgeBaseSearchMutation', () => {
    it('sends POST with query and topK, returns chunks', async () => {
      const mockResponse = { chunks: [{ chunkId: 'c1', content: 'text', score: 0.9, sourceFilename: 'doc.pdf', sourcePath: '/doc.pdf', documentId: 'doc-1' }] };
      mockFetch.mockResolvedValueOnce({ ok: true, json: async () => mockResponse });

      const { result } = renderHook(() => useKnowledgeBaseSearchMutation(), {
        wrapper: createWrapper(),
      });

      let response: any;
      await act(async () => {
        response = await result.current.mutateAsync({ query: 'test query', topK: 3 });
      });

      expect(response).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/search',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ query: 'test query', topK: 3 }),
        })
      );
    });
  });

  describe('useKnowledgeBaseAskMutation', () => {
    it('sends POST with question and returns answer with sources', async () => {
      const mockResponse = { answer: 'The answer is...', sources: [{ documentId: 'd1', filename: 'doc.pdf', excerpt: 'text', score: 0.8 }] };
      mockFetch.mockResolvedValueOnce({ ok: true, json: async () => mockResponse });

      const { result } = renderHook(() => useKnowledgeBaseAskMutation(), {
        wrapper: createWrapper(),
      });

      let response: any;
      await act(async () => {
        response = await result.current.mutateAsync({ question: 'What is X?' });
      });

      expect(response).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/ask',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ question: 'What is X?', topK: 5 }),
        })
      );
    });
  });

  describe('useDeleteKnowledgeBaseDocumentMutation', () => {
    it('sends DELETE request for the document id', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true });

      const { result } = renderHook(() => useDeleteKnowledgeBaseDocumentMutation(), {
        wrapper: createWrapper(),
      });

      await act(async () => {
        await result.current.mutateAsync('doc-1');
      });

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:5001/api/knowledgebase/documents/doc-1',
        { method: 'DELETE' }
      );
    });
  });
});
```

**Step 2: Run test to verify it fails**

```bash
cd frontend && npm test -- useKnowledgeBase --watchAll=false
```

Expected: FAIL — "Cannot find module '../useKnowledgeBase'"

**Step 3: Implement the hook file**

```typescript
// frontend/src/api/hooks/useKnowledgeBase.ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// ---- Types ----

export interface DocumentSummary {
  id: string;
  filename: string;
  status: 'processing' | 'indexed' | 'failed';
  contentType: string;
  createdAt: string;
  indexedAt?: string;
}

export interface ChunkResult {
  chunkId: string;
  documentId: string;
  content: string;
  score: number;
  sourceFilename: string;
  sourcePath: string;
}

export interface SearchResponse {
  chunks: ChunkResult[];
}

export interface SourceReference {
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface AskResponse {
  answer: string;
  sources: SourceReference[];
}

// ---- Query key factory ----

export const knowledgeBaseKeys = {
  documents: ['knowledgeBase', 'documents'] as const,
};

// ---- Hooks ----

export const useKnowledgeBaseDocumentsQuery = () => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents,
    queryFn: async (): Promise<DocumentSummary[]> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
    staleTime: 30 * 1000,
  });
};

export const useKnowledgeBaseSearchMutation = () => {
  return useMutation({
    mutationFn: async ({ query, topK = 5 }: { query: string; topK?: number }): Promise<SearchResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/search`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query, topK }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
  });
};

export const useKnowledgeBaseAskMutation = () => {
  return useMutation({
    mutationFn: async ({ question, topK = 5 }: { question: string; topK?: number }): Promise<AskResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/ask`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, topK }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
  });
};

export const useDeleteKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (documentId: string): Promise<void> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/${documentId}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'DELETE' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.documents });
    },
  });
};
```

**Step 4: Run test to verify it passes**

```bash
cd frontend && npm test -- useKnowledgeBase --watchAll=false
```

Expected: PASS, all 4 describe groups green.

**Step 5: Commit**

```bash
git add frontend/src/api/hooks/useKnowledgeBase.ts frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts
git commit -m "feat: add useKnowledgeBase API hooks with tests"
```

---

### Task 1.3: Create tab components

**Files:**
- Create: `frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`
- Create: `frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx`
- Create: `frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx`

**Step 1: Create KnowledgeBaseDocumentsTab**

```tsx
// frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx
import React, { useState } from 'react';
import { Trash2 } from 'lucide-react';
import { useKnowledgeBaseDocumentsQuery, useDeleteKnowledgeBaseDocumentMutation } from '../../api/hooks/useKnowledgeBase';

const STATUS_COLORS: Record<string, string> = {
  indexed: 'bg-green-100 text-green-800',
  processing: 'bg-yellow-100 text-yellow-800',
  failed: 'bg-red-100 text-red-800',
};

const STATUS_LABELS: Record<string, string> = {
  indexed: 'Indexováno',
  processing: 'Zpracovává se',
  failed: 'Chyba',
};

const KnowledgeBaseDocumentsTab: React.FC = () => {
  const { data: documents, isLoading, isError, refetch } = useKnowledgeBaseDocumentsQuery();
  const deleteMutation = useDeleteKnowledgeBaseDocumentMutation();
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  if (isLoading) {
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => (
          <div key={i} className="h-12 bg-gray-100 rounded animate-pulse" />
        ))}
      </div>
    );
  }

  if (isError) {
    return (
      <div className="text-center py-12">
        <p className="text-red-600 mb-4">Nepodařilo se načíst dokumenty.</p>
        <button onClick={() => refetch()} className="px-4 py-2 text-sm bg-blue-600 text-white rounded hover:bg-blue-700">
          Zkusit znovu
        </button>
      </div>
    );
  }

  if (!documents || documents.length === 0) {
    return (
      <div className="text-center py-12 text-gray-500">
        <p>Žádné dokumenty nejsou indexovány.</p>
        <p className="text-sm mt-1">Dokumenty se načítají z OneDrive každých 15 minut.</p>
      </div>
    );
  }

  const handleDelete = async (id: string) => {
    await deleteMutation.mutateAsync(id);
    setConfirmDeleteId(null);
  };

  return (
    <>
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-gray-200 text-left text-gray-500">
            <th className="pb-2 font-medium">Soubor</th>
            <th className="pb-2 font-medium">Typ</th>
            <th className="pb-2 font-medium">Status</th>
            <th className="pb-2 font-medium">Indexováno</th>
            <th className="pb-2" />
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {documents.map((doc) => (
            <tr key={doc.id} className="hover:bg-gray-50">
              <td className="py-3 font-medium text-gray-900">{doc.filename}</td>
              <td className="py-3 text-gray-500">{doc.contentType}</td>
              <td className="py-3">
                <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_COLORS[doc.status] ?? 'bg-gray-100 text-gray-800'}`}>
                  {STATUS_LABELS[doc.status] ?? doc.status}
                </span>
              </td>
              <td className="py-3 text-gray-500">
                {doc.indexedAt
                  ? new Date(doc.indexedAt).toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' })
                  : '—'}
              </td>
              <td className="py-3 text-right">
                <button
                  onClick={() => setConfirmDeleteId(doc.id)}
                  className="text-gray-400 hover:text-red-600 transition-colors"
                  title="Smazat dokument"
                >
                  <Trash2 size={16} />
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Confirm delete dialog */}
      {confirmDeleteId && (
        <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full mx-4">
            <h3 className="text-lg font-semibold text-gray-900 mb-2">Smazat dokument?</h3>
            <p className="text-sm text-gray-600 mb-6">
              Dokument a všechny jeho indexované části budou trvale odstraněny.
            </p>
            <div className="flex justify-end gap-3">
              <button
                onClick={() => setConfirmDeleteId(null)}
                className="px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded hover:bg-gray-50"
              >
                Zrušit
              </button>
              <button
                onClick={() => handleDelete(confirmDeleteId)}
                disabled={deleteMutation.isPending}
                className="px-4 py-2 text-sm bg-red-600 text-white rounded hover:bg-red-700 disabled:opacity-50"
              >
                {deleteMutation.isPending ? 'Mazání...' : 'Smazat'}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default KnowledgeBaseDocumentsTab;
```

**Step 2: Create KnowledgeBaseSearchTab**

```tsx
// frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx
import React, { useState } from 'react';
import { Search } from 'lucide-react';
import { useKnowledgeBaseSearchMutation } from '../../api/hooks/useKnowledgeBase';

const KnowledgeBaseSearchTab: React.FC = () => {
  const [query, setQuery] = useState('');
  const [topK, setTopK] = useState(5);
  const searchMutation = useKnowledgeBaseSearchMutation();

  const handleSearch = () => {
    if (query.trim()) {
      searchMutation.mutate({ query: query.trim(), topK });
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex gap-3">
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
          placeholder="Hledaný výraz..."
          className="flex-1 px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <div className="flex items-center gap-2 text-sm text-gray-600">
          <label htmlFor="topk-search">Výsledky:</label>
          <input
            id="topk-search"
            type="number"
            min={1}
            max={20}
            value={topK}
            onChange={(e) => setTopK(Number(e.target.value))}
            className="w-16 px-2 py-2 border border-gray-300 rounded text-sm"
          />
        </div>
        <button
          onClick={handleSearch}
          disabled={searchMutation.isPending || !query.trim()}
          className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
        >
          <Search size={16} />
          Hledat
        </button>
      </div>

      {searchMutation.isPending && (
        <div className="text-center py-8 text-gray-500">
          <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600 mx-auto mb-2" />
          Vyhledávám...
        </div>
      )}

      {searchMutation.isError && (
        <p className="text-red-600 text-sm">Vyhledávání selhalo. Zkuste to prosím znovu.</p>
      )}

      {searchMutation.data && (
        <div className="space-y-3">
          {searchMutation.data.chunks.length === 0 ? (
            <p className="text-gray-500 text-sm py-4">Žádné výsledky.</p>
          ) : (
            searchMutation.data.chunks.map((chunk) => (
              <div key={chunk.chunkId} className="border border-gray-200 rounded p-4">
                <div className="flex justify-between items-start mb-2">
                  <span className="text-xs text-gray-500">{chunk.sourceFilename}</span>
                  <span className="text-xs bg-blue-100 text-blue-800 px-2 py-0.5 rounded-full font-medium">
                    {Math.round(chunk.score * 100)}% shoda
                  </span>
                </div>
                <p className="text-sm text-gray-800 leading-relaxed">{chunk.content}</p>
              </div>
            ))
          )}
        </div>
      )}

      {!searchMutation.data && !searchMutation.isPending && !searchMutation.isError && (
        <p className="text-gray-400 text-sm py-8 text-center">Zadejte výraz pro zahájení vyhledávání.</p>
      )}
    </div>
  );
};

export default KnowledgeBaseSearchTab;
```

**Step 3: Create KnowledgeBaseAskTab**

```tsx
// frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx
import React, { useState } from 'react';
import { MessageSquare, ChevronDown, ChevronRight } from 'lucide-react';
import { useKnowledgeBaseAskMutation } from '../../api/hooks/useKnowledgeBase';

const KnowledgeBaseAskTab: React.FC = () => {
  const [question, setQuestion] = useState('');
  const [topK, setTopK] = useState(5);
  const [sourcesExpanded, setSourcesExpanded] = useState(false);
  const askMutation = useKnowledgeBaseAskMutation();

  const handleAsk = () => {
    if (question.trim()) {
      askMutation.mutate({ question: question.trim(), topK });
      setSourcesExpanded(false);
    }
  };

  return (
    <div className="space-y-4 max-w-3xl">
      <div className="space-y-2">
        <textarea
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Zadejte svůj dotaz na firemní dokumenty..."
          rows={3}
          className="w-full px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
        />
        <div className="flex justify-between items-center">
          <div className="flex items-center gap-2 text-sm text-gray-600">
            <label htmlFor="topk-ask">Podkladové části:</label>
            <input
              id="topk-ask"
              type="number"
              min={1}
              max={20}
              value={topK}
              onChange={(e) => setTopK(Number(e.target.value))}
              className="w-16 px-2 py-1 border border-gray-300 rounded text-sm"
            />
          </div>
          <button
            onClick={handleAsk}
            disabled={askMutation.isPending || !question.trim()}
            className="px-4 py-2 bg-blue-600 text-white text-sm rounded hover:bg-blue-700 disabled:opacity-50 flex items-center gap-2"
          >
            <MessageSquare size={16} />
            Zeptat se AI
          </button>
        </div>
      </div>

      {askMutation.isPending && (
        <div className="text-center py-8 text-gray-500">
          <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600 mx-auto mb-2" />
          AI generuje odpověď...
        </div>
      )}

      {askMutation.isError && (
        <p className="text-red-600 text-sm">Dotaz selhal. Zkuste to prosím znovu.</p>
      )}

      {askMutation.data && (
        <div className="space-y-4">
          <div className="bg-blue-50 border border-blue-200 rounded p-4">
            <p className="text-sm text-gray-900 leading-relaxed whitespace-pre-wrap">
              {askMutation.data.answer}
            </p>
          </div>

          {askMutation.data.sources.length > 0 && (
            <div className="border border-gray-200 rounded">
              <button
                onClick={() => setSourcesExpanded(!sourcesExpanded)}
                className="w-full flex items-center justify-between px-4 py-3 text-sm font-medium text-gray-700 hover:bg-gray-50"
              >
                <span>Zdroje ({askMutation.data.sources.length})</span>
                {sourcesExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
              </button>
              {sourcesExpanded && (
                <div className="border-t border-gray-200 divide-y divide-gray-100">
                  {askMutation.data.sources.map((source, idx) => (
                    <div key={idx} className="px-4 py-3">
                      <div className="flex justify-between items-start mb-1">
                        <span className="text-xs font-medium text-gray-700">{source.filename}</span>
                        <span className="text-xs bg-gray-100 text-gray-600 px-2 py-0.5 rounded-full">
                          {Math.round(source.score * 100)}%
                        </span>
                      </div>
                      <p className="text-xs text-gray-500 line-clamp-2">{source.excerpt}</p>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {!askMutation.data && !askMutation.isPending && !askMutation.isError && (
        <p className="text-gray-400 text-sm py-8 text-center">
          Zadejte dotaz a AI vyhledá odpověď ve firemních dokumentech.
        </p>
      )}
    </div>
  );
};

export default KnowledgeBaseAskTab;
```

**Step 4: Run TypeScript check**

```bash
cd frontend && npm run build 2>&1 | grep -E "(error|Error)" | head -20
```

Expected: 0 TypeScript errors.

**Step 5: Commit**

```bash
git add frontend/src/components/knowledge-base/
git commit -m "feat: add KnowledgeBase tab components (Documents, Search, Ask)"
```

---

### Task 1.4: Create KnowledgeBasePage

**Files:**
- Create: `frontend/src/pages/KnowledgeBasePage.tsx`

**Step 1: Create page component**

```tsx
// frontend/src/pages/KnowledgeBasePage.tsx
import React, { useState } from 'react';
import { Database, Search, MessageSquare } from 'lucide-react';
import KnowledgeBaseDocumentsTab from '../components/knowledge-base/KnowledgeBaseDocumentsTab';
import KnowledgeBaseSearchTab from '../components/knowledge-base/KnowledgeBaseSearchTab';
import KnowledgeBaseAskTab from '../components/knowledge-base/KnowledgeBaseAskTab';

type Tab = 'documents' | 'search' | 'ask';

const TABS: { id: Tab; label: string; Icon: React.FC<{ size: number }> }[] = [
  { id: 'documents', label: 'Dokumenty', Icon: Database },
  { id: 'search', label: 'Vyhledávání', Icon: Search },
  { id: 'ask', label: 'Dotaz AI', Icon: MessageSquare },
];

const KnowledgeBasePage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('documents');

  return (
    <div className="flex flex-col h-full p-6">
      <div className="mb-6">
        <h1 className="text-2xl font-semibold text-gray-900 flex items-center gap-2">
          <Database size={24} />
          Znalostní báze
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Firemní dokumenty indexované pro AI vyhledávání
        </p>
      </div>

      <div className="flex gap-1 mb-6 border-b border-gray-200">
        {TABS.map(({ id, label, Icon }) => (
          <button
            key={id}
            onClick={() => setActiveTab(id)}
            className={`flex items-center gap-2 px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
              activeTab === id
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            <Icon size={16} />
            {label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto">
        {activeTab === 'documents' && <KnowledgeBaseDocumentsTab />}
        {activeTab === 'search' && <KnowledgeBaseSearchTab />}
        {activeTab === 'ask' && <KnowledgeBaseAskTab />}
      </div>
    </div>
  );
};

export default KnowledgeBasePage;
```

**Step 2: Run TypeScript check**

```bash
cd frontend && npm run build 2>&1 | grep -E "(error|Error)" | head -20
```

Expected: 0 errors.

**Step 3: Commit**

```bash
git add frontend/src/pages/KnowledgeBasePage.tsx
git commit -m "feat: add KnowledgeBasePage with tab layout"
```

---

### Task 1.5: Register route and navigation

**Files:**
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/Layout/Sidebar.tsx`

**Step 1: Add route to App.tsx**

In `frontend/src/App.tsx`:

1. Add import at top with other page imports (near line 36):
```tsx
import KnowledgeBasePage from './pages/KnowledgeBasePage';
```

2. Add route inside `<Routes>` (before the closing `</Routes>` tag, around line 438):
```tsx
<Route
  path="/knowledge-base"
  element={<KnowledgeBasePage />}
/>
```

**Step 2: Add navigation item to Sidebar.tsx**

In `frontend/src/components/Layout/Sidebar.tsx`:

1. Add `Database` to the lucide-react import (it already imports many icons, just add `Database`):
```tsx
import {
  // ... existing imports ...
  Database,
} from 'lucide-react';
```

2. Add a new navigation section inside `navigationSections` array (after the "automatizace" section, before the closing `]`):
```tsx
{
  id: 'znalostni-baze',
  name: 'Znalostní báze',
  href: '/knowledge-base',
  icon: Database,
  type: 'single' as const,
},
```

**Step 3: Verify locally**

```bash
cd frontend && npm start
```

Navigate to `http://localhost:3000/knowledge-base` — page should render with three tabs.

**Step 4: Run TypeScript build**

```bash
cd frontend && npm run build 2>&1 | grep -E "(error|Error)" | head -20
```

Expected: 0 errors.

**Step 5: Commit**

```bash
git add frontend/src/App.tsx frontend/src/components/Layout/Sidebar.tsx
git commit -m "feat: register /knowledge-base route and nav item"
```

---

## Feature 2: Document Delete (Backend)

> **Note:** If the bug fixes plan Task 3.4 was completed, `IKnowledgeBaseRepository.DeleteDocumentAsync` and its implementation in `KnowledgeBaseRepository` already exist. Skip Tasks 2.1 and 2.2. The frontend delete button was added in Task 1.3 (Documents tab). Only Task 2.3 (the MediatR handler) and Task 2.4 (the controller action) need to be added.

### Task 2.1: Add DeleteDocument use case

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/DeleteDocumentHandlerTests.cs`

**Step 1: Write the failing test**

```csharp
// backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/DeleteDocumentHandlerTests.cs
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class DeleteDocumentHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<ILogger<DeleteDocumentHandler>> _logger = new();

    [Fact]
    public async Task Handle_CallsDeleteDocumentAsync_WithCorrectId()
    {
        var documentId = Guid.NewGuid();
        var request = new DeleteDocumentRequest { DocumentId = documentId };

        _repository
            .Setup(r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new DeleteDocumentHandler(_repository.Object, _logger.Object);

        await handler.Handle(request, CancellationToken.None);

        _repository.Verify(
            r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

**Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test --filter "DeleteDocumentHandlerTests" 2>&1 | tail -10
```

Expected: FAIL — "error CS0246: The type or namespace name 'DeleteDocumentHandler' could not be found"

**Step 3: Implement handler and request**

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentRequest : IRequest
{
    public Guid DocumentId { get; set; }
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentHandler.cs
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentHandler : IRequestHandler<DeleteDocumentRequest>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ILogger<DeleteDocumentHandler> _logger;

    public DeleteDocumentHandler(IKnowledgeBaseRepository repository, ILogger<DeleteDocumentHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(DeleteDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting knowledge base document {DocumentId}", request.DocumentId);
        await _repository.DeleteDocumentAsync(request.DocumentId, cancellationToken);
        _logger.LogInformation("Document {DocumentId} deleted", request.DocumentId);
    }
}
```

**Step 4: Run test to verify it passes**

```bash
cd backend && dotnet test --filter "DeleteDocumentHandlerTests" 2>&1 | tail -10
```

Expected: PASS — 1 test passed.

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/ backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/DeleteDocumentHandlerTests.cs
git commit -m "feat: add DeleteDocument MediatR use case with test"
```

---

### Task 2.2: Add DELETE endpoint to controller

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

**Step 1: Add DELETE action**

Open `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`.

Add the using for the new namespace (if not already a catch-all using):
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
```

Add the action method:
```csharp
[HttpDelete("documents/{id:guid}")]
public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken ct)
{
    await _mediator.Send(new DeleteDocumentRequest { DocumentId = id }, ct);
    return NoContent();
}
```

**Step 2: Build to verify no errors**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
```

Expected: Build succeeded, 0 errors.

**Step 3: Run all tests**

```bash
cd backend && dotnet test 2>&1 | tail -5
```

Expected: All tests pass.

**Step 4: Format**

```bash
cd backend && dotnet format --verify-no-changes
```

If changes are needed: run `dotnet format` then commit.

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "feat: add DELETE /api/knowledgebase/documents/{id} endpoint"
```

---

## Feature 3: Additional Document Formats

### Task 3.1: Update DI and IndexDocumentHandler to support multiple extractors

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`

**Step 1: Update IndexDocumentHandlerTests first (TDD — update test to use IEnumerable)**

Open `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`.

Change how the handler is constructed — pass extractor as an array:
```csharp
// Before:
var handler = new IndexDocumentHandler(_extractor.Object, _embedding.Object, _chunker, _repository.Object);

// After:
var handler = new IndexDocumentHandler(new[] { _extractor.Object }, _embedding.Object, _chunker, _repository.Object);
```

Add a new test for unsupported content type:
```csharp
[Fact]
public async Task Handle_ThrowsNotSupportedException_WhenNoExtractorMatchesContentType()
{
    var request = new IndexDocumentRequest
    {
        Filename = "file.xyz",
        SourcePath = "/file.xyz",
        ContentType = "application/unknown",
        Content = [1, 2, 3],
        ContentHash = "abc123",
    };

    _extractor.Setup(e => e.CanHandle("application/unknown")).Returns(false);

    var handler = new IndexDocumentHandler(new[] { _extractor.Object }, _embedding.Object, _chunker, _repository.Object);

    await Assert.ThrowsAsync<NotSupportedException>(() =>
        handler.Handle(request, CancellationToken.None));
}
```

**Step 2: Run tests to see current state**

```bash
cd backend && dotnet test --filter "IndexDocumentHandlerTests" 2>&1 | tail -10
```

Expected: Existing tests fail due to constructor signature change (planned).

**Step 3: Update IndexDocumentHandler to accept IEnumerable**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`:

Change field:
```csharp
private readonly IEnumerable<IDocumentTextExtractor> _extractors;
```

Change constructor:
```csharp
public IndexDocumentHandler(
    IEnumerable<IDocumentTextExtractor> extractors,
    IEmbeddingService embeddingService,
    DocumentChunker chunker,
    IKnowledgeBaseRepository repository,
    ILogger<IndexDocumentHandler>? logger = null)
{
    _extractors = extractors;
    _embeddingService = embeddingService;
    _chunker = chunker;
    _repository = repository;
    _logger = logger;
}
```

In the `Handle` method, replace the single extractor usage with:
```csharp
var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
    ?? throw new NotSupportedException($"Content type '{request.ContentType}' is not supported.");
var text = await extractor.ExtractTextAsync(request.Content, cancellationToken);
```

**Step 4: Run tests to verify pass**

```bash
cd backend && dotnet test --filter "IndexDocumentHandlerTests" 2>&1 | tail -10
```

Expected: All IndexDocumentHandlerTests pass (including new unsupported type test).

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs
git commit -m "refactor: IndexDocumentHandler accepts IEnumerable<IDocumentTextExtractor>"
```

---

### Task 3.2: Add DocumentFormat.OpenXml NuGet package

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

**Step 1: Add package**

```bash
cd backend/src/Anela.Heblo.Application && dotnet add package DocumentFormat.OpenXml --version 3.1.0
```

**Step 2: Build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
```

Expected: Build succeeded.

**Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
git commit -m "chore: add DocumentFormat.OpenXml for Word document support"
```

---

### Task 3.3: Implement WordDocumentExtractor

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/WordDocumentExtractor.cs`

**Step 1: Write the test first**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/WordDocumentExtractorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class WordDocumentExtractorTests
{
    private readonly WordDocumentExtractor _extractor = new(NullLogger<WordDocumentExtractor>.Instance);

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/msword")]
    [InlineData("APPLICATION/VND.OPENXMLFORMATS-OFFICEDOCUMENT.WORDPROCESSINGML.DOCUMENT")]
    public void CanHandle_ReturnsTrue_ForWordContentTypes(string contentType)
    {
        Assert.True(_extractor.CanHandle(contentType));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("image/png")]
    public void CanHandle_ReturnsFalse_ForNonWordContentTypes(string contentType)
    {
        Assert.False(_extractor.CanHandle(contentType));
    }

    // Note: A real Word document extraction test requires a .docx fixture file.
    // That is covered by integration tests in Task 5.x.
}
```

**Step 2: Run tests to verify they fail**

```bash
cd backend && dotnet test --filter "WordDocumentExtractorTests" 2>&1 | tail -10
```

Expected: FAIL — type not found.

**Step 3: Implement WordDocumentExtractor**

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/WordDocumentExtractor.cs
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class WordDocumentExtractor : IDocumentTextExtractor
{
    private readonly ILogger<WordDocumentExtractor> _logger;

    public WordDocumentExtractor(ILogger<WordDocumentExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.Equals(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from Word document ({Bytes} bytes)", content.Length);

        using var stream = new MemoryStream(content);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult(string.Empty);
        }

        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        var text = string.Join("\n\n", paragraphs);

        _logger.LogDebug("Extracted {CharCount} characters from Word document", text.Length);
        return Task.FromResult(text);
    }
}
```

**Step 4: Run tests to verify they pass**

```bash
cd backend && dotnet test --filter "WordDocumentExtractorTests" 2>&1 | tail -10
```

Expected: PASS.

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/WordDocumentExtractor.cs backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/WordDocumentExtractorTests.cs
git commit -m "feat: add WordDocumentExtractor for .docx support"
```

---

### Task 3.4: Implement PlainTextExtractor

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PlainTextExtractor.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PlainTextExtractorTests.cs`

**Step 1: Write the test**

```csharp
// backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PlainTextExtractorTests.cs
using System.Text;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class PlainTextExtractorTests
{
    private readonly PlainTextExtractor _extractor = new(NullLogger<PlainTextExtractor>.Instance);

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("text/csv")]
    [InlineData("application/markdown")]
    public void CanHandle_ReturnsTrue_ForTextContentTypes(string contentType)
    {
        Assert.True(_extractor.CanHandle(contentType));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("image/png")]
    public void CanHandle_ReturnsFalse_ForNonTextContentTypes(string contentType)
    {
        Assert.False(_extractor.CanHandle(contentType));
    }

    [Fact]
    public async Task ExtractTextAsync_ReturnsUtf8DecodedString()
    {
        const string expected = "Příliš žluťoučký kůň";
        var bytes = Encoding.UTF8.GetBytes(expected);

        var result = await _extractor.ExtractTextAsync(bytes);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ExtractTextAsync_EmptyBytes_ReturnsEmptyString()
    {
        var result = await _extractor.ExtractTextAsync([]);
        Assert.Equal(string.Empty, result);
    }
}
```

**Step 2: Run to verify failure**

```bash
cd backend && dotnet test --filter "PlainTextExtractorTests" 2>&1 | tail -10
```

Expected: FAIL.

**Step 3: Implement PlainTextExtractor**

```csharp
// backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PlainTextExtractor.cs
using System.Text;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class PlainTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PlainTextExtractor> _logger;

    public PlainTextExtractor(ILogger<PlainTextExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/markdown", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from plain text file ({Bytes} bytes)", content.Length);
        return Task.FromResult(Encoding.UTF8.GetString(content));
    }
}
```

**Step 4: Run to verify pass**

```bash
cd backend && dotnet test --filter "PlainTextExtractorTests" 2>&1 | tail -10
```

Expected: PASS.

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PlainTextExtractor.cs backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/PlainTextExtractorTests.cs
git commit -m "feat: add PlainTextExtractor for .txt and text/* support"
```

---

### Task 3.5: Register new extractors in KnowledgeBaseModule

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

**Step 1: Update DI registration**

In `KnowledgeBaseModule.cs`, find where `IDocumentTextExtractor` is registered. It currently looks like:
```csharp
services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
```

Add the two new extractors after it:
```csharp
services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
```

**Step 2: Build and test**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
cd backend && dotnet test 2>&1 | tail -5
```

Expected: 0 errors, all tests pass.

**Step 3: Format**

```bash
cd backend && dotnet format --verify-no-changes
```

**Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git commit -m "feat: register WordDocumentExtractor and PlainTextExtractor in DI"
```

---

## Feature 4: Retry Logic for External APIs

> **Prerequisite:** Bug fixes Tasks 3.1 (AnthropicClaudeService uses IOptions) and 3.2 (OpenAiEmbeddingService uses IOptions + single client instance) must be complete before this feature.

### Task 4.1: Add ResiliencePipeline to OpenAiEmbeddingService

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs`

**Step 1: Verify Polly packages are available**

```bash
cd backend && grep -r "Polly" src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

If `Polly` is missing:
```bash
cd backend/src/Anela.Heblo.Application && dotnet add package Polly --version 8.4.1
```

**Step 2: Add ResiliencePipeline to constructor**

In `OpenAiEmbeddingService.cs`, add a `ResiliencePipeline` field and build it in the constructor:

```csharp
using Polly;
using Polly.Retry;

// Add field:
private readonly ResiliencePipeline _retryPipeline;

// In constructor, after existing initialization:
_retryPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
    })
    .Build();
```

**Step 3: Wrap the API call**

Change `GenerateEmbeddingAsync` to wrap the call:

```csharp
public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
{
    _logger.LogDebug("Generating embedding for {CharCount} characters", text.Length);

    return await _retryPipeline.ExecuteAsync(async token =>
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: token);
        return result.Value.ToFloats().ToArray();
    }, ct);
}
```

**Step 4: Build and test**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
cd backend && dotnet test 2>&1 | tail -5
```

Expected: Build OK, all tests pass.

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs
git commit -m "feat: add Polly retry to OpenAiEmbeddingService"
```

---

### Task 4.2: Add ResiliencePipeline to AnthropicClaudeService

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Step 1: Add ResiliencePipeline field and build in constructor**

Same pattern as Task 4.1. Add field and initialize:
```csharp
using Polly;
using Polly.Retry;

private readonly ResiliencePipeline _retryPipeline;

// In constructor:
_retryPipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
    })
    .Build();
```

**Step 2: Wrap GenerateAnswerAsync**

Find the internal `api.CreateMessageAsync(...)` call and wrap it:

```csharp
public async Task<string> GenerateAnswerAsync(
    string question,
    IEnumerable<string> contextChunks,
    CancellationToken ct = default)
{
    // ... build prompt ...

    return await _retryPipeline.ExecuteAsync(async token =>
    {
        var message = await api.CreateMessageAsync(..., cancellationToken: token);
        return /* extract text from response */;
    }, ct);
}
```

> Note: Wrap only the actual API call (`api.CreateMessageAsync`), not the prompt-building logic.

**Step 3: Build and test**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
cd backend && dotnet test 2>&1 | tail -5
```

**Step 4: Format**

```bash
cd backend && dotnet format --verify-no-changes
```

**Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs
git commit -m "feat: add Polly retry to AnthropicClaudeService"
```

---

## Feature 5: Integration Tests

### Task 5.1: Check existing integration test infrastructure

**Step 1: Check for existing Testcontainers usage**

```bash
grep -r "Testcontainers" backend/test/Anela.Heblo.Tests/ --include="*.csproj" --include="*.cs" -l
```

If no results, proceed to Task 5.2. If Testcontainers is already present, skip 5.2 and go straight to 5.3.

**Step 2: Check test project .csproj for existing base classes**

Read `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` to see current NuGet packages.

---

### Task 5.2: Add Testcontainers NuGet package

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

**Step 1: Add package**

```bash
cd backend/test/Anela.Heblo.Tests && dotnet add package Testcontainers.PostgreSql --version 3.10.0
```

**Step 2: Build**

```bash
cd backend && dotnet build 2>&1 | grep -E "^.*error" | head -10
```

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "chore: add Testcontainers.PostgreSql for integration tests"
```

---

### Task 5.3: Add DocumentChunker edge case tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs`

**Step 1: Write new test cases**

Open `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs` and add:

```csharp
[Fact]
public void Chunk_CzechUnicodeText_ChunksCorrectly()
{
    var text = string.Join(" ", Enumerable.Repeat("příliš", 600));
    var chunker = new DocumentChunker();

    var chunks = chunker.Chunk(text);

    Assert.True(chunks.Count >= 1);
    Assert.All(chunks, c => Assert.Contains("příliš", c));
}

[Fact]
public void Chunk_SingleWord_ReturnsSingleChunk()
{
    var chunker = new DocumentChunker();
    var chunks = chunker.Chunk("hello");

    Assert.Single(chunks);
    Assert.Equal("hello", chunks[0]);
}

[Fact]
public void Chunk_ExactlyChunkSizeWords_ReturnsSingleChunk()
{
    const int chunkSize = 512;
    var words = Enumerable.Repeat("word", chunkSize);
    var text = string.Join(" ", words);
    var chunker = new DocumentChunker();

    var chunks = chunker.Chunk(text);

    Assert.Single(chunks);
}

[Fact]
public void Chunk_ExactlyChunkSizePlusOneWord_ReturnsTwoChunks()
{
    const int chunkSize = 512;
    var words = Enumerable.Repeat("word", chunkSize + 1);
    var text = string.Join(" ", words);
    var chunker = new DocumentChunker();

    var chunks = chunker.Chunk(text);

    Assert.Equal(2, chunks.Count);
}

[Fact]
public void Chunk_VeryLongDocument_ProducesExpectedChunkCount()
{
    const int wordCount = 10_000;
    const int chunkSize = 512;
    const int overlap = 50;
    var text = string.Join(" ", Enumerable.Repeat("word", wordCount));
    var chunker = new DocumentChunker();

    var chunks = chunker.Chunk(text);

    // Expected: ceil(wordCount / (chunkSize - overlap)) approximately
    Assert.True(chunks.Count >= 20, $"Expected at least 20 chunks, got {chunks.Count}");
}
```

**Step 2: Run tests**

```bash
cd backend && dotnet test --filter "DocumentChunkerTests" 2>&1 | tail -10
```

Expected: All pass (including existing tests).

**Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs
git commit -m "test: add DocumentChunker edge case tests (unicode, boundaries)"
```

---

### Task 5.4: Create KnowledgeBaseRepository integration tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`

**Step 1: Determine ApplicationDbContext type**

```bash
grep -r "class.*DbContext" backend/src/Anela.Heblo.Persistence/ --include="*.cs" | head -5
```

Note the exact class name (likely `ApplicationDbContext`).

**Step 2: Write the integration test**

```csharp
// backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Anela.Heblo.Tests.KnowledgeBase.Integration;

[Trait("Category", "Integration")]
public class KnowledgeBaseRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private ApplicationDbContext _context = null!;
    private KnowledgeBaseRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseVector())
            .Options;

        _context = new ApplicationDbContext(options);
        await _context.Database.MigrateAsync();
        _repository = new KnowledgeBaseRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task AddDocumentAndChunks_CanBeRetrievedByHash()
    {
        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = "test.pdf",
            SourcePath = "/inbox/test.pdf",
            ContentType = "application/pdf",
            ContentHash = "abc123hash",
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
            IndexedAt = DateTime.UtcNow,
        };

        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var found = await _repository.GetDocumentByHashAsync("abc123hash");

        Assert.NotNull(found);
        Assert.Equal(doc.Id, found!.Id);
        Assert.Equal("test.pdf", found.Filename);
    }

    [Fact]
    public async Task DeleteDocumentAsync_RemovesDocument()
    {
        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = "delete-me.pdf",
            SourcePath = "/inbox/delete-me.pdf",
            ContentType = "application/pdf",
            ContentHash = "deletehash",
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
        };

        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        await _repository.DeleteDocumentAsync(doc.Id);

        var allDocs = await _repository.GetAllDocumentsAsync();
        Assert.DoesNotContain(allDocs, d => d.Id == doc.Id);
    }

    [Fact]
    public async Task GetDocumentBySourcePathAsync_ReturnsCorrectDocument()
    {
        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = "path-test.pdf",
            SourcePath = "/inbox/specific-path.pdf",
            ContentType = "application/pdf",
            ContentHash = "pathhash",
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
        };

        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var found = await _repository.GetDocumentBySourcePathAsync("/inbox/specific-path.pdf");

        Assert.NotNull(found);
        Assert.Equal(doc.Id, found!.Id);
    }
}
```

**Step 3: Run integration tests (requires Docker)**

```bash
cd backend && dotnet test --filter "Category=Integration" 2>&1 | tail -20
```

Expected: All 3 tests pass. Docker must be running.

**Step 4: Verify unit tests still pass**

```bash
cd backend && dotnet test --filter "Category!=Integration" 2>&1 | tail -5
```

**Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/
git commit -m "test: add KnowledgeBaseRepository integration tests with Testcontainers"
```

---

## Final Checks

After all tasks are complete:

**Step 1: Backend build and all tests**

```bash
cd backend && dotnet build
cd backend && dotnet format --verify-no-changes
cd backend && dotnet test --filter "Category!=Integration" 2>&1 | tail -5
```

Expected: 0 errors, 0 format violations, all tests pass.

**Step 2: Frontend build and all tests**

```bash
cd frontend && npm run build 2>&1 | grep -E "error" | head -10
cd frontend && npm test -- --watchAll=false --passWithNoTests 2>&1 | tail -10
cd frontend && npm run lint
```

Expected: 0 TypeScript errors, all tests pass, 0 lint violations.

**Step 3: Manual smoke test checklist**

- [ ] Navigate to `/knowledge-base` — page renders with 3 tabs
- [ ] Documents tab — loads list with status badges
- [ ] Documents tab — delete button shows confirmation dialog, document removed after confirm
- [ ] Search tab — enter query, results show with score badges
- [ ] Ask tab — enter question, answer renders in blue box, sources expand on click
- [ ] Upload a `.docx` to OneDrive inbox → after 15 min → appears as `indexed`
- [ ] Upload a `.txt` to OneDrive inbox → after 15 min → appears as `indexed`
- [ ] `DELETE /api/knowledgebase/documents/{id}` returns 204
