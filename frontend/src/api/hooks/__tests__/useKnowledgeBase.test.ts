import { renderHook, waitFor } from '@testing-library/react';
import {
  useKnowledgeBaseDocumentsQuery,
  useKnowledgeBaseContentTypesQuery,
  useKnowledgeBaseSearchMutation,
  useChunkDetailQuery,
  useKnowledgeBaseAskMutation,
  useDeleteKnowledgeBaseDocumentMutation,
  useSubmitFeedbackMutation,
  useUploadKnowledgeBaseDocumentMutation,
  useKnowledgeBaseFeedbackListQuery,
  knowledgeBaseKeys,
} from '../useKnowledgeBase';
import { SearchDocumentsRequest, AskQuestionRequest, SubmitFeedbackRequest } from '../../generated/api-client';
import { mockAuthenticatedApiClient, createQueryClientWrapper } from '../../testUtils';

jest.mock('../../client');

describe('useKnowledgeBaseDocumentsQuery', () => {
  let mockClient: { knowledgeBase_GetDocuments: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_GetDocuments: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('calls knowledgeBase_GetDocuments with the given params in declared order', async () => {
    mockClient.knowledgeBase_GetDocuments.mockResolvedValue({
      documents: [{ id: 'doc-1', filename: 'safety-data.pdf', status: 'indexed', contentType: 'application/pdf' }],
      totalCount: 1,
      pageNumber: 2,
      pageSize: 10,
      totalPages: 1,
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useKnowledgeBaseDocumentsQuery({
          pageNumber: 2,
          pageSize: 10,
          sortBy: 'Filename',
          sortDescending: false,
          filenameFilter: 'report',
          statusFilter: 'indexed',
          contentTypeFilter: 'application/pdf',
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_GetDocuments).toHaveBeenCalledWith(
      2,
      10,
      'Filename',
      false,
      'report',
      'indexed',
      'application/pdf',
    );
    expect(result.current.data?.documents).toHaveLength(1);
    expect(result.current.data?.totalCount).toBe(1);
  });

  it('passes undefined for omitted params', async () => {
    mockClient.knowledgeBase_GetDocuments.mockResolvedValue({
      documents: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0,
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useKnowledgeBaseDocumentsQuery(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_GetDocuments).toHaveBeenCalledWith(
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
    );
  });
});

describe('useKnowledgeBaseContentTypesQuery', () => {
  let mockClient: { knowledgeBase_GetDocumentContentTypes: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_GetDocumentContentTypes: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('calls knowledgeBase_GetDocumentContentTypes with no args', async () => {
    mockClient.knowledgeBase_GetDocumentContentTypes.mockResolvedValue({
      contentTypes: ['application/pdf', 'text/plain'],
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useKnowledgeBaseContentTypesQuery(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_GetDocumentContentTypes).toHaveBeenCalledWith();
    expect(result.current.data?.contentTypes).toHaveLength(2);
  });
});

describe('useKnowledgeBaseSearchMutation', () => {
  let mockClient: { knowledgeBase_Search: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_Search: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('sends a SearchDocumentsRequest and returns chunks', async () => {
    mockClient.knowledgeBase_Search.mockResolvedValue({
      chunks: [
        {
          chunkId: 'chunk-1',
          documentId: 'doc-1',
          content: 'Max phenoxyethanol 1.0% per EU regulation',
          score: 0.92,
          sourceFilename: 'EU_reg.pdf',
          sourcePath: '/archived/EU_reg.pdf',
        },
      ],
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useKnowledgeBaseSearchMutation(), { wrapper });

    result.current.mutate({ query: 'phenoxyethanol limit', topK: 3 });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_Search).toHaveBeenCalledWith(
      new SearchDocumentsRequest({ query: 'phenoxyethanol limit', topK: 3 }),
    );
    expect(result.current.data?.chunks).toHaveLength(1);
    expect(result.current.data?.chunks?.[0].score).toBe(0.92);
  });

  it('defaults topK to 5 when omitted', async () => {
    mockClient.knowledgeBase_Search.mockResolvedValue({ chunks: [] });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useKnowledgeBaseSearchMutation(), { wrapper });

    result.current.mutate({ query: 'phenoxyethanol' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_Search).toHaveBeenCalledWith(
      new SearchDocumentsRequest({ query: 'phenoxyethanol', topK: 5 }),
    );
  });
});

describe('useChunkDetailQuery', () => {
  let mockClient: { knowledgeBase_GetChunkDetail: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_GetChunkDetail: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('calls knowledgeBase_GetChunkDetail with the chunk id', async () => {
    mockClient.knowledgeBase_GetChunkDetail.mockResolvedValue({
      chunkId: 'chunk-1',
      documentId: 'doc-1',
      filename: 'conversation.txt',
      documentType: 'Conversation',
      chunkIndex: 0,
      summary: 'Summary',
      content: 'Content',
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useChunkDetailQuery('chunk-1'), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_GetChunkDetail).toHaveBeenCalledWith('chunk-1');
  });

  it('does not fire when chunkId is null', () => {
    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useChunkDetailQuery(null), { wrapper });

    expect(result.current.fetchStatus).toBe('idle');
    expect(mockClient.knowledgeBase_GetChunkDetail).not.toHaveBeenCalled();
  });
});

describe('useKnowledgeBaseAskMutation', () => {
  let mockClient: { knowledgeBase_Ask: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_Ask: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('sends an AskQuestionRequest and returns answer with sources', async () => {
    mockClient.knowledgeBase_Ask.mockResolvedValue({
      id: 'log-1',
      answer: 'The maximum allowed concentration is 1.0%.',
      sources: [
        { chunkId: 'chunk-1', documentId: 'doc-1', filename: 'EU_reg.pdf', excerpt: 'Max phenoxyethanol 1.0%', score: 0.95 },
      ],
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useKnowledgeBaseAskMutation(), { wrapper });

    result.current.mutate({ question: 'What is the max phenoxyethanol?', topK: 5 });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_Ask).toHaveBeenCalledWith(
      new AskQuestionRequest({ question: 'What is the max phenoxyethanol?', topK: 5 }),
    );
    expect(result.current.data?.answer).toBe('The maximum allowed concentration is 1.0%.');
    expect(result.current.data?.sources).toHaveLength(1);
  });
});

describe('useDeleteKnowledgeBaseDocumentMutation', () => {
  let mockClient: { knowledgeBase_DeleteDocument: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_DeleteDocument: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('calls knowledgeBase_DeleteDocument with the document id and invalidates the list', async () => {
    mockClient.knowledgeBase_DeleteDocument.mockResolvedValue({});

    const { wrapper, queryClient } = createQueryClientWrapper();
    const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useDeleteKnowledgeBaseDocumentMutation(), { wrapper });

    result.current.mutate('doc-abc-123');

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_DeleteDocument).toHaveBeenCalledWith('doc-abc-123');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: knowledgeBaseKeys.all });
  });
});

describe('useSubmitFeedbackMutation', () => {
  let mockClient: { knowledgeBase_SubmitFeedback: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_SubmitFeedback: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('calls the typed knowledgeBase_SubmitFeedback method and returns {} on success', async () => {
    mockClient.knowledgeBase_SubmitFeedback.mockResolvedValue({});

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useSubmitFeedbackMutation(), { wrapper });

    result.current.mutate({ logId: 'log-1', precisionScore: 4, styleScore: 5, comment: 'Great' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_SubmitFeedback).toHaveBeenCalledWith(
      new SubmitFeedbackRequest({ logId: 'log-1', precisionScore: 4, styleScore: 5, comment: 'Great' }),
    );
    expect(result.current.data).toEqual({});
  });

  it('returns { alreadySubmitted: true } on a 409 instead of throwing', async () => {
    mockClient.knowledgeBase_SubmitFeedback.mockRejectedValue({ status: 409 });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useSubmitFeedbackMutation(), { wrapper });

    result.current.mutate({ logId: 'log-1', precisionScore: 4, styleScore: 5 });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual({ alreadySubmitted: true });
  });

  it('rethrows for a non-409 error', async () => {
    mockClient.knowledgeBase_SubmitFeedback.mockRejectedValue({ status: 500 });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useSubmitFeedbackMutation(), { wrapper });

    result.current.mutate({ logId: 'log-1', precisionScore: 4, styleScore: 5 });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('useUploadKnowledgeBaseDocumentMutation', () => {
  let mockClient: { knowledgeBase_UploadDocument: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_UploadDocument: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('wraps the file into a FileParameter and invalidates the list on success', async () => {
    const file = new File(['pdf content'], 'guide.pdf', { type: 'application/pdf' });
    mockClient.knowledgeBase_UploadDocument.mockResolvedValue({
      document: { id: 'new-doc-1', filename: 'guide.pdf', status: 'indexed', contentType: 'application/pdf' },
    });

    const { wrapper, queryClient } = createQueryClientWrapper();
    const invalidateSpy = jest.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useUploadKnowledgeBaseDocumentMutation(), { wrapper });

    result.current.mutate({ file, documentType: 'KnowledgeBase' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_UploadDocument).toHaveBeenCalledWith(
      { data: file, fileName: 'guide.pdf' },
      'KnowledgeBase',
    );
    expect(result.current.data?.document?.filename).toBe('guide.pdf');
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: knowledgeBaseKeys.all });
  });
});

describe('useKnowledgeBaseFeedbackListQuery', () => {
  let mockClient: { knowledgeBase_GetFeedbackList: jest.Mock };

  beforeEach(() => {
    jest.clearAllMocks();
    mockClient = { knowledgeBase_GetFeedbackList: jest.fn() };
    mockAuthenticatedApiClient(mockClient);
  });

  it('calls knowledgeBase_GetFeedbackList with the given params', async () => {
    mockClient.knowledgeBase_GetFeedbackList.mockResolvedValue({
      logs: [],
      totalCount: 0,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 0,
      stats: { totalQuestions: 0, totalWithFeedback: 0 },
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(
      () =>
        useKnowledgeBaseFeedbackListQuery({
          pageNumber: 1,
          pageSize: 20,
          sortBy: 'CreatedAt',
          sortDescending: true,
          hasFeedback: true,
          userId: 'user-1',
        }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockClient.knowledgeBase_GetFeedbackList).toHaveBeenCalledWith(1, 20, 'CreatedAt', true, true, 'user-1');
  });

  it('maps generated Date/undefined fields into the local string/null shape', async () => {
    const createdAt = new Date('2026-03-01T10:00:00Z');
    const sentAt = new Date('2026-03-01T10:05:00Z');
    mockClient.knowledgeBase_GetFeedbackList.mockResolvedValue({
      logs: [
        {
          id: 'log-1',
          feature: 'KnowledgeBase',
          question: 'What is the return policy?',
          answer: 'Within 14 days.',
          expandedQuery: undefined,
          systemPrompt: 'You are a helpful assistant.',
          retrievedChunks: [
            { chunkId: 'chunk-1', documentId: 'doc-1', filename: 'policy.pdf', score: 0.9, content: 'chunk text' },
          ],
          topK: 5,
          sourceCount: 1,
          durationMs: 120,
          createdAt,
          userId: undefined,
          userName: undefined,
          conversationId: undefined,
          topic: undefined,
          sentAnswer: undefined,
          wasEdited: undefined,
          sentAt,
          precisionScore: 4,
          styleScore: undefined,
          feedbackComment: undefined,
          hasFeedback: true,
        },
      ],
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
      totalPages: 1,
      stats: {
        totalQuestions: 10,
        totalWithFeedback: 2,
        avgPrecisionScore: 3.5,
        avgStyleScore: undefined,
      },
    });

    const { wrapper } = createQueryClientWrapper();
    const { result } = renderHook(() => useKnowledgeBaseFeedbackListQuery(), { wrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const log = result.current.data?.logs[0];
    expect(log?.createdAt).toBe(createdAt.toISOString());
    expect(log?.sentAt).toBe(sentAt.toISOString());
    expect(log?.expandedQuery).toBeNull();
    expect(log?.userId).toBeNull();
    expect(log?.wasEdited).toBeNull();
    expect(log?.styleScore).toBeNull();
    expect(log?.retrievedChunks).toEqual([
      { chunkId: 'chunk-1', documentId: 'doc-1', filename: 'policy.pdf', score: 0.9, content: 'chunk text' },
    ]);
    expect(result.current.data?.stats.avgStyleScore).toBeNull();
    expect(result.current.data?.stats.avgPrecisionScore).toBe(3.5);
  });
});
