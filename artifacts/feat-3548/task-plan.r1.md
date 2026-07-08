# feat-3548 Implementation Plan

**Goal:** Make `HtmlContent` in `ArticleDetail.tsx` derive `isDark` from the reactive `useTheme()` hook instead of a non-reactive `document.documentElement.classList` read, so the article iframe's colors update immediately when the user toggles the app theme.

**Architecture:** No architectural change. `HtmlContent` is a private helper function inside `frontend/src/features/articles/ArticleDetail.tsx`; it swaps its theme source for the existing `useTheme()` hook from `frontend/src/contexts/ThemeContext.tsx` (already the single source of theme truth used elsewhere, e.g. `ThemeToggle.tsx`, `OrgChartPage.tsx`). The existing `key={isDark ? 'dark' : 'light'}` remount mechanism on the `<iframe>` is preserved untouched — it starts working correctly because its input (`isDark`) becomes real React state instead of a DOM snapshot.

**Tech Stack:** React 18 + TypeScript (frontend), Jest + React Testing Library (`react-scripts test`), existing `ThemeContext` (React Context + `useState`).

---

### task: fix-articledetail-theme-reactivity

**Files:**
- Modify: `frontend/src/features/articles/ArticleDetail.tsx`
- Create: `frontend/src/features/articles/__tests__/ArticleDetail.test.tsx`

- [ ] **Step 1: Write a failing regression test proving the iframe doesn't update on theme toggle (RED)**

  Create `frontend/src/features/articles/__tests__/ArticleDetail.test.tsx` with the following content:

  ```tsx
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

      const iframeBefore = document.querySelector('iframe') as HTMLIFrameElement;
      expect(iframeBefore).toBeInTheDocument();
      expect(iframeBefore.srcdoc).toContain('#1f2937'); // light body text color
      expect(iframeBefore.srcdoc).not.toContain('#E6E8EC');

      act(() => {
        screen.getByText('toggle-theme').click();
      });

      const iframeAfter = document.querySelector('iframe') as HTMLIFrameElement;
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

      const iframe = document.querySelector('iframe') as HTMLIFrameElement;
      expect(iframe.srcdoc).toContain('#1f2937');
      expect(iframe.srcdoc).not.toContain('#E6E8EC');
    });
  });
  ```

  Run:
  ```
  cd frontend && CI=true npx react-scripts test src/features/articles/__tests__/ArticleDetail.test.tsx --watchAll=false
  ```

  **Expected output:** Both tests in this file FAIL. With the current (buggy) code, `ArticleDetail`/`HtmlContent` never subscribes to `ThemeContext`, so clicking `toggle-theme` does not cause `HtmlContent` to re-render at all — `iframeAfter.srcdoc` still contains `#1f2937` and not `#E6E8EC`, failing the first test's post-toggle assertions. This confirms the test correctly reproduces the bug before touching the fix.

- [ ] **Step 2: Apply the fix (GREEN)**

  In `frontend/src/features/articles/ArticleDetail.tsx`, add the import (after the existing `ARTICLE_STATUS_LABELS` import on line 7):

  ```tsx
  import { ARTICLE_STATUS_LABELS, ARTICLE_STATUS_COLORS } from './articleStatusConfig';
  import { useTheme } from '../../contexts/ThemeContext';
  ```

  Then replace line 14 (`const isDark = document.documentElement.classList.contains('dark');`) inside `HtmlContent`:

  **Before:**
  ```tsx
  function HtmlContent({ html }: { html: string }) {
    const isDark = document.documentElement.classList.contains('dark');
  ```

  **After:**
  ```tsx
  function HtmlContent({ html }: { html: string }) {
    const { theme } = useTheme();
    const isDark = theme === 'dark';
  ```

  No other line in `ArticleDetail.tsx` changes.

- [ ] **Step 3: Verify the fix (tests, build, lint)**

  Run, in order, and confirm each succeeds:
  ```
  cd frontend && CI=true npx react-scripts test src/features/articles/__tests__/ArticleDetail.test.tsx --watchAll=false
  ```
  **Expected output:** Both tests in `ArticleDetail.test.tsx` now PASS.

  ```
  cd frontend && CI=true npx react-scripts test --watchAll=false
  ```
  **Expected output:** Full frontend test suite passes (no regressions in other suites, including `src/contexts/__tests__/ThemeContext.test.tsx`).

  ```
  cd frontend && grep -n "document.documentElement.classList" src/features/articles/ArticleDetail.tsx
  ```
  **Expected output:** No matches (empty output / exit code 1) — confirms acceptance criterion "`HtmlContent` no longer references `document.documentElement.classList` anywhere in its body."

  ```
  cd frontend && npm run build
  ```
  **Expected output:** Build succeeds with no new TypeScript errors.

  ```
  cd frontend && npm run lint
  ```
  **Expected output:** No new lint errors introduced by this change.

- [ ] **Step 4: Commit**

  ```
  git add frontend/src/features/articles/ArticleDetail.tsx frontend/src/features/articles/__tests__/ArticleDetail.test.tsx
  git commit -m "fix(articles): derive HtmlContent theme from useTheme() instead of DOM read"
  ```
