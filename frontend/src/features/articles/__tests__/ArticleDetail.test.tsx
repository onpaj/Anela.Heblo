import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ArticleDetail from '../ArticleDetail';
import * as useArticlesHook from '../../../api/hooks/useArticles';
import { ArticleStatus } from '../../../api/generated/api-client';

jest.mock('../../../components/knowledge-base/ChunkDetailModal', () => ({
  __esModule: true,
  default: ({ chunkId, onClose }: { chunkId: string; onClose: () => void }) => (
    <div data-testid="chunk-modal" data-chunk-id={chunkId}>
      <button onClick={onClose}>Close</button>
    </div>
  ),
}));

jest.mock('../../../api/hooks/useArticles', () => ({
  ...jest.requireActual('../../../api/hooks/useArticles'),
  useGetArticleQuery: jest.fn(),
  useSubmitArticleFeedbackMutation: jest.fn(() => ({
    mutate: jest.fn(),
    isPending: false,
    isSuccess: false,
    isError: false,
  })),
}));

const baseArticle = {
  id: 'art-1',
  topic: 'SPF Guide',
  scope: 'overview',
  audience: null,
  angle: null,
  length: 'medium (1000w)',
  title: 'SPF Guide',
  htmlContent: '<p>Content</p>',
  status: ArticleStatus.Generated,
  errorMessage: null,
  createdAt: '2026-05-06T10:00:00Z',
  generatedAt: '2026-05-06T10:05:00Z',
  useKnowledgeBase: true,
  useWebSearch: false,
  sources: [],
  precisionScore: null,
  styleScore: null,
  feedbackComment: null,
};

beforeEach(() => {
  jest.clearAllMocks();
  (useArticlesHook.useSubmitArticleFeedbackMutation as jest.Mock).mockReturnValue({
    mutate: jest.fn(),
    isPending: false,
    isSuccess: false,
    isError: false,
  });
});

test('shows feedback form when Generated and no scores', () => {
  (useArticlesHook.useGetArticleQuery as jest.Mock).mockReturnValue({
    data: baseArticle,
    isLoading: false,
    error: null,
  });

  render(<ArticleDetail articleId="art-1" />);

  expect(screen.getByRole('button', { name: /odeslat/i })).toBeInTheDocument();
});

test('hides feedback form and shows summary when scores already set', () => {
  (useArticlesHook.useGetArticleQuery as jest.Mock).mockReturnValue({
    data: { ...baseArticle, precisionScore: 4, styleScore: 3 },
    isLoading: false,
    error: null,
  });

  render(<ArticleDetail articleId="art-1" />);

  expect(screen.getByText(/Hodnocení: Přesnost 4\/5, Styl 3\/5/i)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /odeslat/i })).not.toBeInTheDocument();
});

test('renders KB source as clickable button when chunkId is set', () => {
  (useArticlesHook.useGetArticleQuery as jest.Mock).mockReturnValue({
    data: {
      ...baseArticle,
      sources: [
        { title: 'KB Doc', url: null, type: 'KnowledgeBase', knowledgeBaseChunkId: 'chunk-42', excerpt: null },
      ],
    },
    isLoading: false,
    error: null,
  });

  render(<ArticleDetail articleId="art-1" />);

  const button = screen.getByRole('button', { name: 'KB Doc' });
  expect(button).toBeInTheDocument();
});

test('clicking KB source button opens ChunkDetailModal', () => {
  (useArticlesHook.useGetArticleQuery as jest.Mock).mockReturnValue({
    data: {
      ...baseArticle,
      sources: [
        { title: 'KB Doc', url: null, type: 'KnowledgeBase', knowledgeBaseChunkId: 'chunk-42', excerpt: null },
      ],
    },
    isLoading: false,
    error: null,
  });

  render(<ArticleDetail articleId="art-1" />);

  fireEvent.click(screen.getByRole('button', { name: 'KB Doc' }));

  const modal = screen.getByTestId('chunk-modal');
  expect(modal).toBeInTheDocument();
  expect(modal).toHaveAttribute('data-chunk-id', 'chunk-42');
});

test('closing ChunkDetailModal removes it from DOM', () => {
  (useArticlesHook.useGetArticleQuery as jest.Mock).mockReturnValue({
    data: {
      ...baseArticle,
      sources: [
        { title: 'KB Doc', url: null, type: 'KnowledgeBase', knowledgeBaseChunkId: 'chunk-42', excerpt: null },
      ],
    },
    isLoading: false,
    error: null,
  });

  render(<ArticleDetail articleId="art-1" />);

  fireEvent.click(screen.getByRole('button', { name: 'KB Doc' }));
  expect(screen.getByTestId('chunk-modal')).toBeInTheDocument();

  fireEvent.click(screen.getByRole('button', { name: 'Close' }));
  expect(screen.queryByTestId('chunk-modal')).not.toBeInTheDocument();
});
