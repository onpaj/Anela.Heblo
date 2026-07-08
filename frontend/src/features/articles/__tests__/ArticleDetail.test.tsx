import { render, screen, act } from '@testing-library/react';
import ArticleDetail from '../ArticleDetail';
import { useGetArticleQuery } from '../../../api/hooks/useArticles';
import { ArticleStatus } from '../../../api/generated/api-client';
import { ThemeProvider, useTheme } from '../../../contexts/ThemeContext';

// setupTests.ts globally mocks ThemeContext with a fixed theme; restore the
// real implementation so this test can exercise actual theme toggling.
jest.unmock('../../../contexts/ThemeContext');

jest.mock('../../../api/hooks/useArticles', () => ({
  ...jest.requireActual('../../../api/hooks/useArticles'),
  useGetArticleQuery: jest.fn(),
}));

// Isolate the test from unrelated subcomponents that make their own API calls.
jest.mock('../ArticleSourceList', () => () => null);
jest.mock('../ArticleFeedbackSection', () => () => null);
jest.mock('../ArticleDebugPanel', () => () => null);

const mockedUseGetArticleQuery = useGetArticleQuery as jest.Mock;

const ARTICLE = {
  id: 'article-1',
  topic: 'Test topic',
  scope: 'scope',
  audience: null,
  angle: null,
  length: 'short',
  title: 'Test title',
  htmlContent: '<p>Hello world</p>',
  status: ArticleStatus.Generated,
  errorMessage: null,
  createdAt: new Date().toISOString(),
  generatedAt: new Date().toISOString(),
  useKnowledgeBase: false,
  useWebSearch: false,
  sources: [],
  precisionScore: null,
  styleScore: null,
  feedbackComment: null,
};

// Mirrors how ThemeToggle drives useTheme() elsewhere in the app: a real
// consumer that calls toggle() on the real ThemeProvider.
function ToggleButton() {
  const { toggle } = useTheme();
  return <button onClick={toggle}>toggle-theme</button>;
}

const renderWithTheme = () =>
  render(
    <ThemeProvider>
      <ToggleButton />
      <ArticleDetail articleId="article-1" />
    </ThemeProvider>,
  );

describe('ArticleDetail HtmlContent theme reactivity', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.classList.remove('dark');
    mockedUseGetArticleQuery.mockReturnValue({
      data: ARTICLE,
      isLoading: false,
      error: null,
    });
  });

  it('remounts the article iframe with dark colors after the theme is toggled to dark', () => {
    renderWithTheme();

    const iframeBefore = screen.getByTitle('Obsah článku') as HTMLIFrameElement;
    expect(iframeBefore).toBeInTheDocument();
    expect(iframeBefore.srcdoc).toContain('#1f2937'); // light body text color
    expect(iframeBefore.srcdoc).not.toContain('#E6E8EC');

    act(() => {
      screen.getByText('toggle-theme').click();
    });

    const iframeAfter = screen.getByTitle('Obsah článku') as HTMLIFrameElement;
    expect(iframeAfter).toBeInTheDocument();
    expect(iframeAfter.srcdoc).toContain('#E6E8EC'); // dark body text color
    expect(iframeAfter.srcdoc).not.toContain('#1f2937');
  });

  it('remounts the article iframe back to light colors after toggling twice', () => {
    renderWithTheme();

    act(() => {
      screen.getByText('toggle-theme').click(); // -> dark
    });
    act(() => {
      screen.getByText('toggle-theme').click(); // -> light
    });

    const iframe = screen.getByTitle('Obsah článku') as HTMLIFrameElement;
    expect(iframe.srcdoc).toContain('#1f2937');
    expect(iframe.srcdoc).not.toContain('#E6E8EC');
  });
});
