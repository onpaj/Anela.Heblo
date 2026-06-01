import React from 'react';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ArticleFeedbackSection from '../ArticleFeedbackSection';
import { ArticleDetail } from '../../../api/hooks/useArticles';
import { ArticleStatus } from '../../../api/generated/api-client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: () => ({
    baseUrl: 'http://test',
    http: { fetch: jest.fn() },
  }),
  QUERY_KEYS: { articles: ['articles'] },
}));

function renderWithClient(node: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={queryClient}>{node}</QueryClientProvider>);
}

const baseArticle: ArticleDetail = {
  id: 'a1',
  topic: 't',
  scope: 's',
  audience: null,
  angle: null,
  length: 'medium (1000w)',
  title: null,
  htmlContent: null,
  status: ArticleStatus.Generated,
  errorMessage: null,
  createdAt: '2026-05-06T00:00:00Z',
  generatedAt: null,
  useKnowledgeBase: false,
  useWebSearch: false,
  sources: [],
  precisionScore: null,
  styleScore: null,
  feedbackComment: null,
};

describe('ArticleFeedbackSection', () => {
  it('renders nothing when article status is not Generated', () => {
    const article = { ...baseArticle, status: ArticleStatus.Writing };
    const { container } = renderWithClient(<ArticleFeedbackSection article={article} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders the form when scores are null', () => {
    renderWithClient(<ArticleFeedbackSection article={baseArticle} />);
    expect(screen.getByText(/Ohodnoťte odpověď/)).toBeInTheDocument();
  });

  it('renders read-only summary when scores are set', () => {
    const article = {
      ...baseArticle,
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'Loved it',
    };
    renderWithClient(<ArticleFeedbackSection article={article} />);
    expect(
      screen.getByText('Hodnocení: Přesnost 4/5, Styl 5/5'),
    ).toBeInTheDocument();
    expect(screen.getByText('Loved it')).toBeInTheDocument();
  });

  it('renders summary without comment block when comment is empty', () => {
    const article = { ...baseArticle, precisionScore: 3, styleScore: 3, feedbackComment: '' };
    renderWithClient(<ArticleFeedbackSection article={article} />);
    expect(screen.getByText('Hodnocení: Přesnost 3/5, Styl 3/5')).toBeInTheDocument();
    expect(screen.queryByTestId('article-feedback-comment')).not.toBeInTheDocument();
  });
});
