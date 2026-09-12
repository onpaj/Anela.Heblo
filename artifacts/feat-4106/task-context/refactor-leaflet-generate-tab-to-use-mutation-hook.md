### task: refactor-leaflet-generate-tab-to-use-mutation-hook

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` (full file, currently 97 lines)
- Test: `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`

This task depends on `add-generate-leaflet-mutation-hook` being committed first (`useGenerateLeafletMutation` must already exist and be exported from `frontend/src/api/hooks/useLeaflet.ts`).

- [ ] **Step 1: Refactor the component to use the new hook**

Replace the entire contents of `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` with:

```tsx
import React, { useState } from 'react';
import LeafletForm from './LeafletForm';
import LeafletResult from './LeafletResult';
import { useGenerateLeafletMutation } from '../../api/hooks/useLeaflet';
import {
  AudienceType,
  ErrorCodes,
  GenerateLeafletResponse,
  LeafletLength,
} from '../../api/generated/api-client';

interface ErrorBanner {
  kind: 'insufficient' | 'transient';
  message: string;
}

const LeafletGenerateTab: React.FC = () => {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
  const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
  const [result, setResult] = useState('');
  const [generationId, setGenerationId] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);
  const generateLeaflet = useGenerateLeafletMutation();

  const generate = async () => {
    setGenerationId(null);
    setErrorBanner(null);
    try {
      const response = await generateLeaflet.mutateAsync({ topic, audience, length });
      setResult(response.content ?? '');
      setGenerationId(response.id ?? null);
    } catch (err: unknown) {
      if (err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval) {
        setErrorBanner({
          kind: 'insufficient',
          message: 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
        });
      } else {
        setErrorBanner({
          kind: 'transient',
          message: 'Generování selhalo. Zkuste to prosím znovu.',
        });
      }
    }
  };

  return (
    <>
      {errorBanner && (
        <div
          role="alert"
          className={`mb-4 rounded p-3 text-sm ${
            errorBanner.kind === 'insufficient'
              ? 'bg-amber-100 text-amber-900 dark:bg-amber-900/30 dark:text-amber-300'
              : 'bg-red-100 text-red-900 dark:bg-red-900/30 dark:text-red-300'
          }`}
        >
          {errorBanner.message}
        </div>
      )}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div>
          <LeafletForm
            topic={topic}
            audience={audience}
            length={length}
            isLoading={generateLeaflet.isPending}
            onTopicChange={setTopic}
            onAudienceChange={setAudience}
            onLengthChange={setLength}
            onSubmit={generate}
          />
        </div>
        <div>
          {generateLeaflet.isPending ? (
            <div className="animate-pulse space-y-2">
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-3/4" />
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded" />
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-5/6" />
            </div>
          ) : (
            <LeafletResult content={result} generationId={generationId} onRegenerate={generate} />
          )}
        </div>
      </div>
    </>
  );
};

export default LeafletGenerateTab;
```

This removes the `getAuthenticatedApiClient` and `GenerateLeafletRequest` imports, removes the local `isLoading` `useState` and both `setIsLoading` calls (and the now-unneeded `finally` block), and replaces both `isLoading` read sites with `generateLeaflet.isPending`.

- [ ] **Step 2: Run the existing (not-yet-updated) test file to confirm it now fails**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`

Expected: FAIL. The test file's `jest.mock('../../../api/hooks/useLeaflet', () => ({ useSubmitLeafletFeedbackMutation: () => ({...}) }))` replaces the whole `useLeaflet` module, so the component's imported `useGenerateLeafletMutation` resolves to `undefined`. Calling it during render throws `TypeError: (0 , _useLeaflet.useGenerateLeafletMutation) is not a function`, and both test cases fail with this error.

- [ ] **Step 3: Update the test file's render wrapper and module mock**

Replace the entire contents of `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` with:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import '@testing-library/jest-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import LeafletGenerateTab from '../LeafletGenerateTab';
import { getAuthenticatedApiClient } from '../../../api/client';
import { ErrorCodes, GenerateLeafletResponse } from '../../../api/generated/api-client';

jest.mock('../../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
}));

jest.mock('../../../api/hooks/useLeaflet', () => ({
  ...jest.requireActual('../../../api/hooks/useLeaflet'),
  useSubmitLeafletFeedbackMutation: () => ({
    mutate: jest.fn(),
    isPending: false,
    isError: false,
  }),
}));

jest.mock('react-markdown', () => ({
  __esModule: true,
  default: ({ children }: { children: string }) => <div>{children}</div>,
}));

let mockGenerate: jest.Mock;

beforeEach(() => {
  jest.clearAllMocks();
  mockGenerate = jest.fn();
  (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
    leaflet_Generate: mockGenerate,
  });
});

function createWrapper({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}

async function fillAndSubmit() {
  fireEvent.change(screen.getByLabelText('Téma'), { target: { value: 'Bisabolol' } });
  fireEvent.click(screen.getByRole('button', { name: 'Vygenerovat leták' }));
}

describe('LeafletGenerateTab', () => {
  it('shows the insufficient knowledge banner when the API rejects with LeafletEmptyRetrieval', async () => {
    const errorResponse = new GenerateLeafletResponse({
      success: false,
      errorCode: ErrorCodes.LeafletEmptyRetrieval,
    });
    mockGenerate.mockRejectedValue(errorResponse);

    render(<LeafletGenerateTab />, { wrapper: createWrapper });
    await fillAndSubmit();

    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.');
    expect(banner.className).toContain('bg-amber-100');
  });

  it('shows the transient failure banner for a generic error', async () => {
    mockGenerate.mockRejectedValue(new Error('network down'));

    render(<LeafletGenerateTab />, { wrapper: createWrapper });
    await fillAndSubmit();

    const banner = await screen.findByRole('alert');
    expect(banner).toHaveTextContent('Generování selhalo. Zkuste to prosím znovu.');
    expect(banner.className).toContain('bg-red-100');
  });
});
```

The two test bodies and assertions are unchanged from the original file — only the added `QueryClientProvider` wrapper (`createWrapper`) and the `jest.requireActual`-based mock (which keeps the real `useGenerateLeafletMutation` while still stubbing `useSubmitLeafletFeedbackMutation` for the child `LeafletResult` component) are new.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd frontend && CI=true npm test -- --watchAll=false src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`

Expected: PASS — both test cases pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx
git commit -m "$(cat <<'EOF'
Refactor LeafletGenerateTab to use useGenerateLeafletMutation

Replaces the inline getAuthenticatedApiClient().leaflet_Generate(...) call
and hand-rolled isLoading state with the new useGenerateLeafletMutation hook,
using mutateAsync inside the existing try/catch so the reset/error-classification
control flow is unchanged. Test file now wraps render in a QueryClientProvider
and requires the actual useLeaflet module so the real hook runs in tests.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01GfeMm8RYfo3pHeyGsa52Dd
EOF
)"
```

---

