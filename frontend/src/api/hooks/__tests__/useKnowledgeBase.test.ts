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
  return ({ children }: { children: React.ReactNode }) =>
    React.createElement(QueryClientProvider, { client: queryClient }, children);
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
