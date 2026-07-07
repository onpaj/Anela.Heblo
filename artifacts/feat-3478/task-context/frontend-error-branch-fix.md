### task: frontend-error-branch-fix

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`
- Test: `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx` (new file)

Changing the `422` `[ProducesResponseType]` from `ProblemDetails` to `GenerateLeafletResponse` (previous controller task) changes what the generated TypeScript client parses and throws on a 422 response. Regenerate the client first so the exact generated shape is known, then fix the one component that special-cased the old shape, verified with a new test file (none existed for this component before).

- [ ] Step 1: Regenerate the OpenAPI TypeScript client from the now-updated backend (requires the backend to build successfully, which it does after the previous tasks):
  ```bash
  dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
  ```
- [ ] Step 2: Confirm the regenerated `frontend/src/api/generated/api-client.ts` now has `LeafletEmptyRetrieval` in the `ErrorCodes` TS enum, and that `processLeaflet_Generate`'s `422` branch now parses the body as `GenerateLeafletResponse.fromJS(...)` instead of `ProblemDetails.fromJS(...)`:
  ```bash
  grep -n "LeafletEmptyRetrieval" frontend/src/api/generated/api-client.ts
  grep -n -A6 'status === 422' frontend/src/api/generated/api-client.ts | grep -A6 "processLeaflet_Generate" -m1
  ```
  If the `422` branch still says `ProblemDetails.fromJS`, stop — the backend attribute change (`controller-drops-trycatch` task) or the regeneration step didn't take effect; re-run step 1 after confirming `dotnet build backend/Anela.Heblo.API` succeeds.
- [ ] Step 3 (failing test): Create `frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx`:
  ```tsx
  import React from 'react';
  import { render, screen, fireEvent } from '@testing-library/react';
  import '@testing-library/jest-dom';
  import LeafletGenerateTab from '../LeafletGenerateTab';
  import { getAuthenticatedApiClient } from '../../../api/client';
  import { ErrorCodes, GenerateLeafletResponse } from '../../../api/generated/api-client';

  jest.mock('../../../api/client', () => ({
    getAuthenticatedApiClient: jest.fn(),
  }));

  const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.Mock;

  const fillTopicAndSubmit = () => {
    fireEvent.change(screen.getByLabelText('Téma'), { target: { value: 'Bisabolol' } });
    fireEvent.click(screen.getByRole('button', { name: 'Vygenerovat leták' }));
  };

  describe('LeafletGenerateTab', () => {
    let mockGenerate: jest.Mock;

    beforeEach(() => {
      jest.clearAllMocks();
      mockGenerate = jest.fn();
      mockGetAuthenticatedApiClient.mockReturnValue({ leaflet_Generate: mockGenerate });
    });

    it('shows the amber insufficient-knowledge banner when the server returns LeafletEmptyRetrieval', async () => {
      const errorResponse = new GenerateLeafletResponse();
      errorResponse.success = false;
      errorResponse.errorCode = ErrorCodes.LeafletEmptyRetrieval;
      mockGenerate.mockRejectedValue(errorResponse);

      render(<LeafletGenerateTab />);
      fillTopicAndSubmit();

      const banner = await screen.findByRole('alert');
      expect(banner).toHaveTextContent(
        'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.'
      );
      expect(banner.className).toContain('bg-amber-100');
    });

    it('shows the red transient banner for any other thrown error', async () => {
      mockGenerate.mockRejectedValue(new Error('network error'));

      render(<LeafletGenerateTab />);
      fillTopicAndSubmit();

      const banner = await screen.findByRole('alert');
      expect(banner).toHaveTextContent('Generování selhalo. Zkuste to prosím znovu.');
      expect(banner.className).toContain('bg-red-100');
    });
  });
  ```
- [ ] Step 4: Run the new test file to confirm it fails against the current component (it still checks `isApiError(err) && err.status === 422`, and the mocked rejected `GenerateLeafletResponse` instance has no `.status` field, so both tests would currently show the red "transient" banner — the first test's assertion on `bg-amber-100` will fail):
  ```bash
  cd frontend && npx react-scripts test src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx --watchAll=false
  ```
  Expect the first test (`shows the amber insufficient-knowledge banner...`) to fail.
- [ ] Step 5 (implementation): Open `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`. Current content:
  ```tsx
  import React, { useState } from 'react';
  import LeafletForm from './LeafletForm';
  import LeafletResult from './LeafletResult';
  import { getAuthenticatedApiClient } from '../../api/client';
  import { AudienceType, GenerateLeafletRequest, LeafletLength } from '../../api/generated/api-client';

  interface ErrorBanner {
    kind: 'insufficient' | 'transient';
    message: string;
  }

  interface ApiError {
    status: number;
    detail?: string;
  }

  function isApiError(err: unknown): err is ApiError {
    return typeof err === 'object' && err !== null && typeof (err as Record<string, unknown>)['status'] === 'number';
  }

  const LeafletGenerateTab: React.FC = () => {
    const [topic, setTopic] = useState('');
    const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
    const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
    const [result, setResult] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [generationId, setGenerationId] = useState<string | null>(null);
    const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);

    const generate = async () => {
      setIsLoading(true);
      setGenerationId(null);
      setErrorBanner(null);
      try {
        const client = getAuthenticatedApiClient();
        const response = await client.leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }));
        setResult(response.content ?? '');
        setGenerationId((response as any).id ?? null);
      } catch (err: unknown) {
        if (isApiError(err) && err.status === 422) {
          setErrorBanner({
            kind: 'insufficient',
            message:
              err.detail ??
              'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
          });
        } else {
          setErrorBanner({
            kind: 'transient',
            message: 'Generování selhalo. Zkuste to prosím znovu.',
          });
        }
      } finally {
        setIsLoading(false);
      }
    };
  ```
  Replace it with:
  ```tsx
  import React, { useState } from 'react';
  import LeafletForm from './LeafletForm';
  import LeafletResult from './LeafletResult';
  import { getAuthenticatedApiClient } from '../../api/client';
  import {
    AudienceType,
    ErrorCodes,
    GenerateLeafletRequest,
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
    const [isLoading, setIsLoading] = useState(false);
    const [generationId, setGenerationId] = useState<string | null>(null);
    const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);

    const generate = async () => {
      setIsLoading(true);
      setGenerationId(null);
      setErrorBanner(null);
      try {
        const client = getAuthenticatedApiClient();
        const response = await client.leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }));
        setResult(response.content ?? '');
        setGenerationId((response as any).id ?? null);
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
      } finally {
        setIsLoading(false);
      }
    };
  ```
  The rest of the file (the JSX in the `return (...)` block) is unchanged. The now-unused `ApiError` interface and `isApiError` function are removed — nothing else in the file used them.
- [ ] Step 6: Run the new test file again to confirm both tests now pass:
  ```bash
  cd frontend && npx react-scripts test src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx --watchAll=false
  ```
- [ ] Step 7: Run the full frontend test suite to confirm no regression elsewhere (in particular `LeafletGeneratorPage.test.tsx`, which mocks `LeafletGenerateTab` wholesale and is unaffected):
  ```bash
  cd frontend && npm test -- --watchAll=false
  ```
- [ ] Step 8: Run the frontend build and lint (required by this repo's validation gate):
  ```bash
  cd frontend && npm run build && npm run lint
  ```
- [ ] Step 9: Commit (include the regenerated `api-client.ts`, since the frontend build depends on it being in sync with the backend contract):
  ```bash
  git add frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx frontend/src/features/leaflet-generator/__tests__/LeafletGenerateTab.test.tsx frontend/src/api/generated/api-client.ts
  git commit -m "#3478: LeafletGenerateTab detects LeafletEmptyRetrieval via errorCode instead of HTTP status"
  ```

---

## Final verification (after all tasks)

- [ ] `dotnet build Anela.Heblo.sln` and `dotnet format Anela.Heblo.sln` — both clean.
- [ ] `dotnet test Anela.Heblo.sln` — full backend suite green.
- [ ] `cd frontend && npm run build && npm run lint` — both clean.
- [ ] `cd frontend && npm test -- --watchAll=false` — full frontend suite green.
- [ ] `grep -rn "EmptyRetrievalException" backend/` returns no matches anywhere in the repository.
- [ ] Manual sanity check of the acceptance criteria in `artifacts/feat-3478/spec.r1.md`: FR-1 (enum member + TS enum), FR-2 (response ctor), FR-3 (handler returns, doesn't throw), FR-4 (controller has no try/catch, signature matches), FR-5 (MCP tool inspects `response.Success`), FR-6 (exception type deleted), FR-7 (frontend banner logic), FR-8 (all four listed test files updated, no test deleted without a replacement) are each satisfied by the task(s) above.
