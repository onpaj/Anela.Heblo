### task: replace-apifetch-in-usecatalogdocuments

**Files:**
- Modify: `frontend/src/api/hooks/useCatalogDocuments.ts` (full rewrite, lines 1-163)
- Modify: `frontend/src/components/catalog/detail/tabs/shared/DocumentList.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx`
- Test: `frontend/src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx`
- Test: `frontend/src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx`
- Not modified (verified compatible, run only): `frontend/src/components/catalog/detail/tabs/shared/MaterialUploadDialog.tsx`, `frontend/src/components/catalog/detail/tabs/shared/PifUploadDialog.tsx`, `frontend/src/components/catalog/detail/tabs/shared/__tests__/MaterialUploadDialog.test.tsx`

#### Goal
Bring `useCatalogDocuments.ts` into compliance with `docs/development/api-client-generation.md` by calling the generated `ApiClient`'s typed `catalogDocuments_*` methods instead of the private-field reach-around, deleting the duplicate hand-rolled DTOs, and fixing up the small number of downstream files whose compilation depends on those DTOs' exact shape.

#### Context (from spec.r1.md / arch-review.r1.md / design.r1.md, verified against current code)
- `frontend/src/api/generated/api-client.ts` already has all five needed methods: `catalogDocuments_ListMaterialDocuments(productCode: string): Promise<ListCatalogDocumentsResponse>` (line 2376), `catalogDocuments_UploadMaterialDocument(productCode: string, file: FileParameter | null | undefined, documentTypeCode: string | null | undefined, lot: string | null | undefined, commonName: string | null | undefined, uploadAsIs: boolean | undefined): Promise<UploadDocumentResponse>` (line 2413), `catalogDocuments_ListPifDocuments(productCode: string): Promise<ListCatalogDocumentsResponse>` (line 2465), `catalogDocuments_UploadPifDocument(productCode: string, file: FileParameter | null | undefined): Promise<UploadDocumentResponse>` (line 2502), `catalogDocuments_GetMaterialDocumentTypes(): Promise<GetMaterialDocumentTypesResponse>` (line 2544). No regeneration needed.
- Generated `ListCatalogDocumentsResponse` (line ~19740): `folderStatus?: FolderStatus; expectedPrefix?: string; basePath?: string; files?: CatalogDocumentDto[];` plus inherited `success?: boolean` from `BaseResponse`.
- Generated `FolderStatus` (line ~19793) is a real TypeScript **enum**: `enum FolderStatus { Found = "Found", NotFound = "NotFound", MultipleMatches = "MultipleMatches" }` — NOT a string-literal union like the current hand-rolled `type FolderStatus = 'Found' | 'NotFound' | 'MultipleMatches'`. Verified with `tsc --strict`: comparing an enum value to a matching string literal (`status === 'Found'`) compiles fine, but *assigning* a plain string literal to an enum-typed variable/prop/argument (`const x: FolderStatus = 'NotFound'`, or passing `"Found"` as a `FolderStatus`-typed prop) does **not** compile (`TS2345`/`TS2322`).
- Generated `CatalogDocumentDto` (line ~19799): `name?: string; webUrl?: string; sizeBytes?: number; modifiedAt?: Date;` — every field optional, and `modifiedAt` is a real `Date` (the class's `init()` does `new Date(_data["modifiedAt"].toString())`), not a `string` like the current hand-rolled interface says. It is a **class** with instance methods `init()`/`toJSON()`, not a plain interface — a bare object literal (`{ name: '...', modifiedAt: '...' }`) does **not** structurally satisfy this type in strict mode (verified with `tsc --strict`: `TS2322`, missing `init`/`toJSON`); constructing via `new CatalogDocumentDto({...})` does satisfy it (verified clean compile).
- Generated `GetMaterialDocumentTypesResponse` (line ~19847): `documentTypes?: MaterialDocumentTypeDto[];` plus inherited `success?`. `MaterialDocumentTypeDto` (line ~19888): `code?: string; label?: string; lotRequired?: boolean;`.
- Generated `UploadDocumentResponse` (line ~19932): `uploadedFilename?: string;` plus inherited `success?: boolean; errorCode?: ErrorCodes; params?: { [key: string]: string; }` from `BaseResponse` (line 13704) — matches the hand-rolled shape field-for-field.
- `FileParameter` (line ~44557): `interface FileParameter { data: any; fileName: string; }`.
- Sibling reference pattern: `frontend/src/api/hooks/useKnowledgeBase.ts` — `queryFn`/`mutationFn` do `const apiClient = getAuthenticatedApiClient(); return apiClient.<op>(...);` with an explicit `Promise<T>` return-type annotation on the function, and `useUploadKnowledgeBaseDocumentMutation` builds `const fileParameter: FileParameter = { data: file, fileName: file.name };` inline. Mirror this style exactly.
- **Verified consumer blast radius** (grep for `useCatalogDocuments`, `CatalogDocumentDto`, `FolderStatus` across `frontend/src`):
  - `MaterialDocumentsTab.tsx`, `PifDocumentsTab.tsx` — import hooks only, plus `const folderStatus = data?.folderStatus ?? 'NotFound';` (this line needs a fix — see Step 6/7).
  - `MaterialUploadDialog.tsx`, `PifUploadDialog.tsx` — import hooks only, read fields structurally (`.success`, `.documentTypes`, `t.code`, `t.label`, `t.lotRequired`) — no changes needed, confirmed compiles unchanged.
  - `DocumentList.tsx` (shared, used by both tabs) — imports `CatalogDocumentDto` **type by name** from the hook file, and does `formatFileSize(file.sizeBytes)` (now needs a `number | undefined` guard) and `new Date(file.modifiedAt).toLocaleDateString(...)` (now needs a `Date | undefined` guard, and the `new Date(...)` wrapper is redundant since `modifiedAt` is already a `Date`). Needs fixing — see Step 4.
  - `FolderStatusBanner.tsx` (shared, used by both tabs) — imports `FolderStatus` **type by name** from the hook file. Needs its import source updated — see Step 5.
  - `DocumentList.test.tsx` — imports `CatalogDocumentDto` type from the hook file and builds a plain object literal typed as `CatalogDocumentDto` with a string `modifiedAt` — breaks under the generated class type. Needs fixing — see Step 8.
  - `FolderStatusBanner.test.tsx` — passes plain string literals (`status="Found"` etc.) as the `status` prop — breaks once the prop type is the generated enum. Needs fixing — see Step 9.
  - `MaterialUploadDialog.test.tsx` — mocks the **hook module** itself (`jest.mock('.../useCatalogDocuments')`) and casts stub return values with `as any`; never touches `getAuthenticatedApiClient()`, `fetch`, or the deleted types by name. Confirmed no changes needed; run only, to confirm it still passes.

#### Implementation steps

- [ ] **Step 1: Establish a baseline — run the existing tests for every file this task will touch or could affect**

Run:
```bash
cd frontend
CI=true npx react-scripts test --testPathPattern="catalog/detail" --watchAll=false
```
Expected: all existing suites (`DocumentList.test.tsx`, `FolderStatusBanner.test.tsx`, `MaterialUploadDialog.test.tsx`) PASS. This confirms the starting point before any edits.

- [ ] **Step 2: Rewrite `useCatalogDocuments.ts` to call the generated client and delete the duplicate DTOs**

Replace the entire contents of `frontend/src/api/hooks/useCatalogDocuments.ts` with:

```ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  type ListCatalogDocumentsResponse,
  type GetMaterialDocumentTypesResponse,
  type UploadDocumentResponse,
  type FileParameter,
} from '../generated/api-client';

export interface UploadMaterialDocumentParams {
  productCode: string;
  file: File;
  documentTypeCode: string;
  lot: string;
  commonName: string;
  uploadAsIs: boolean;
}

export interface UploadPifDocumentParams {
  productCode: string;
  file: File;
}

const catalogDocumentsKeys = {
  materialDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'materials', productCode] as const,
  pifDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'pif', productCode] as const,
  materialDocumentTypes: () =>
    [...QUERY_KEYS.catalogDocuments, 'material-document-types'] as const,
};

export function useMaterialDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocuments(productCode),
    queryFn: (): Promise<ListCatalogDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalogDocuments_ListMaterialDocuments(productCode);
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function usePifDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.pifDocuments(productCode),
    queryFn: (): Promise<ListCatalogDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalogDocuments_ListPifDocuments(productCode);
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function useMaterialDocumentTypes() {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocumentTypes(),
    queryFn: (): Promise<GetMaterialDocumentTypesResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalogDocuments_GetMaterialDocumentTypes();
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useUploadMaterialDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (params: UploadMaterialDocumentParams): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const file: FileParameter = { data: params.file, fileName: params.file.name };
      return apiClient.catalogDocuments_UploadMaterialDocument(
        params.productCode,
        file,
        params.documentTypeCode,
        params.lot,
        params.commonName,
        params.uploadAsIs,
      );
    },
    retry: 0,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.materialDocuments(variables.productCode),
      });
    },
  });
}

export function useUploadPifDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (params: UploadPifDocumentParams): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const file: FileParameter = { data: params.file, fileName: params.file.name };
      return apiClient.catalogDocuments_UploadPifDocument(params.productCode, file);
    },
    retry: 0,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.pifDocuments(variables.productCode),
      });
    },
  });
}
```

This deletes `apiFetch`, `FolderStatus`, `CatalogDocumentDto`, `MaterialDocumentTypeDto`, `ListCatalogDocumentsResponse`, `GetMaterialDocumentTypesResponse`, and `UploadDocumentResponse` (the hand-rolled versions) entirely. `UploadMaterialDocumentParams`, `UploadPifDocumentParams`, and `catalogDocumentsKeys` are kept verbatim (byte-for-byte identical to the original), per spec FR-2/FR-3 and design.r1.md.

- [ ] **Step 3: Run the build to see the expected downstream breakage**

Run:
```bash
cd frontend
npx tsc --noEmit -p tsconfig.json
```
Expected: FAILS with errors in `DocumentList.tsx` and `DocumentList.test.tsx` (`Cannot find name 'CatalogDocumentDto'` / module has no exported member), `FolderStatusBanner.tsx` (`Cannot find name 'FolderStatus'`), and `MaterialDocumentsTab.tsx`/`PifDocumentsTab.tsx` (`Argument of type '"NotFound"' is not assignable to parameter of type 'FolderStatus'` once Step 6/7 partially lands — for now, before those steps, expect the `Cannot find name` errors from the two `shared/` files). This confirms the blast radius identified above and gives a checklist to clear in the next steps.

- [ ] **Step 4: Fix `DocumentList.tsx` — import the generated type and guard the now-optional fields**

Replace the top of `frontend/src/components/catalog/detail/tabs/shared/DocumentList.tsx` (the import line) and the two `<span>` lines inside the list item, so the full file reads:

```tsx
import { type CatalogDocumentDto } from '../../../../../api/generated/api-client';

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

interface DocumentListProps {
  files: CatalogDocumentDto[];
  isLoading: boolean;
  onUploadClick?: () => void;
}

export default function DocumentList({ files, isLoading, onUploadClick }: DocumentListProps) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-8 text-gray-500 dark:text-graphite-muted text-sm">
        Načítání…
      </div>
    );
  }

  if (files.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-8 gap-3 text-gray-500 dark:text-graphite-muted text-sm">
        <span>Žádné dokumenty</span>
        {onUploadClick && (
          <button
            onClick={onUploadClick}
            className="text-indigo-600 hover:text-indigo-800 text-sm font-medium dark:text-graphite-accent"
          >
            Nahrát soubor
          </button>
        )}
      </div>
    );
  }

  return (
    <ul className="divide-y divide-gray-100 dark:divide-graphite-border">
      {files.map((file) => (
        <li key={file.webUrl} className="flex items-center justify-between py-3 px-1 hover:bg-gray-50 rounded dark:hover:bg-white/5">
          <a
            href={file.webUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-sm text-indigo-600 hover:text-indigo-800 hover:underline truncate max-w-xs dark:text-graphite-accent"
            title={file.name}
          >
            {file.name}
          </a>
          <div className="flex items-center gap-4 text-xs text-gray-500 ml-4 shrink-0 dark:text-graphite-muted">
            <span>{formatFileSize(file.sizeBytes ?? 0)}</span>
            <span>{file.modifiedAt ? file.modifiedAt.toLocaleDateString('cs-CZ') : ''}</span>
          </div>
        </li>
      ))}
    </ul>
  );
}
```

Only two behavioral lines changed: `formatFileSize(file.sizeBytes ?? 0)` (was `formatFileSize(file.sizeBytes)`) and `{file.modifiedAt ? file.modifiedAt.toLocaleDateString('cs-CZ') : ''}` (was `{new Date(file.modifiedAt).toLocaleDateString('cs-CZ')}`) — `file.modifiedAt` is already a `Date` object from the generated client's `fromJS`, so the `new Date(...)` wrapper is removed as redundant, and both are now guarded because the generated `CatalogDocumentDto` marks these fields optional. In real production responses the backend always populates them, so this is a compile-time-only accommodation with no behavioral change for real data.

- [ ] **Step 5: Fix `FolderStatusBanner.tsx` — import the generated enum type**

In `frontend/src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx`, change only the import line:

```tsx
import { type FolderStatus } from '../../../../../api/generated/api-client';
```

(was `import type { FolderStatus } from '../../../../../api/hooks/useCatalogDocuments';`). The rest of the file (the `status === 'Found'` / `status === 'MultipleMatches'` comparisons and JSX) is unchanged — verified these comparisons still compile against the enum type.

- [ ] **Step 6: Fix `MaterialDocumentsTab.tsx` — default to the enum member instead of a string literal**

In `frontend/src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx`, add an import and change the `folderStatus` default:

```tsx
import { useState } from 'react';
import { RefreshCw, Upload } from 'lucide-react';
import { useMaterialDocuments } from '../../../../api/hooks/useCatalogDocuments';
import { FolderStatus } from '../../../../api/generated/api-client';
import DocumentList from './shared/DocumentList';
import FolderStatusBanner from './shared/FolderStatusBanner';
import MaterialUploadDialog from './shared/MaterialUploadDialog';
```

and:

```tsx
  const folderStatus = data?.folderStatus ?? FolderStatus.NotFound;
```

(was `const folderStatus = data?.folderStatus ?? 'NotFound';`). Everything else in the file (the `folderStatus === 'Found'` checks, the `<FolderStatusBanner status={folderStatus} .../>` usage) is unchanged — verified these still compile once `folderStatus`'s type is the enum rather than a `FolderStatus | "NotFound"` union.

- [ ] **Step 7: Fix `PifDocumentsTab.tsx` — same enum-default fix**

In `frontend/src/components/catalog/detail/tabs/PifDocumentsTab.tsx`, add an import and change the `folderStatus` default:

```tsx
import { useState } from 'react';
import { RefreshCw, Upload } from 'lucide-react';
import { usePifDocuments } from '../../../../api/hooks/useCatalogDocuments';
import { FolderStatus } from '../../../../api/generated/api-client';
import DocumentList from './shared/DocumentList';
import FolderStatusBanner from './shared/FolderStatusBanner';
import PifUploadDialog from './shared/PifUploadDialog';
```

and:

```tsx
  const folderStatus = data?.folderStatus ?? FolderStatus.NotFound;
```

(was `const folderStatus = data?.folderStatus ?? 'NotFound';`). Everything else unchanged.

- [ ] **Step 8: Fix `DocumentList.test.tsx` — construct real `CatalogDocumentDto` instances**

Replace the top of `frontend/src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx` (imports and `makeFile` helper) so the full file reads:

```tsx
import { render, screen } from '@testing-library/react';
import DocumentList from '../DocumentList';
import { CatalogDocumentDto, type ICatalogDocumentDto } from '../../../../../../api/generated/api-client';

const makeFile = (overrides?: Partial<ICatalogDocumentDto>): CatalogDocumentDto =>
  new CatalogDocumentDto({
    name: 'COA__L001__Bisabolol.pdf',
    webUrl: 'https://sp.example.com/file.pdf',
    sizeBytes: 102400,
    modifiedAt: new Date('2026-05-01T12:00:00Z'),
    ...overrides,
  });

describe('DocumentList', () => {
  it('shows empty state when no files', () => {
    render(<DocumentList files={[]} isLoading={false} />);
    expect(screen.getByText(/Žádné dokumenty/i)).toBeInTheDocument();
  });

  it('shows loading state', () => {
    render(<DocumentList files={[]} isLoading={true} />);
    expect(screen.getByText(/Načítání/i)).toBeInTheDocument();
  });

  it('renders filename and size', () => {
    render(<DocumentList files={[makeFile()]} isLoading={false} />);
    expect(screen.getByText('COA__L001__Bisabolol.pdf')).toBeInTheDocument();
    expect(screen.getByText(/100 KB/i)).toBeInTheDocument();
  });

  it('renders a link that opens webUrl in new tab', () => {
    render(<DocumentList files={[makeFile()]} isLoading={false} />);
    const link = screen.getByRole('link', { name: /COA__L001__Bisabolol.pdf/i });
    expect(link).toHaveAttribute('href', 'https://sp.example.com/file.pdf');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', 'noopener noreferrer');
  });
});
```

`new CatalogDocumentDto({...})` produces a real class instance (with `init`/`toJSON` on its prototype), which satisfies the generated class type under `strict: true` — verified by compiling this exact pattern with `tsc --strict`. `modifiedAt` is now a real `Date` (`new Date('2026-05-01T12:00:00Z')`) matching the generated field's type. All assertions are unchanged — same rendered output as before.

- [ ] **Step 9: Fix `FolderStatusBanner.test.tsx` — pass the generated enum members**

Replace the full contents of `frontend/src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import FolderStatusBanner from '../FolderStatusBanner';
import { FolderStatus } from '../../../../../../api/generated/api-client';

describe('FolderStatusBanner', () => {
  it('renders nothing when status is Found', () => {
    const { container } = render(
      <FolderStatusBanner status={FolderStatus.Found} expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('shows not-found message with prefix and basePath', () => {
    render(
      <FolderStatusBanner status={FolderStatus.NotFound} expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(screen.getByText(/MAT001__/)).toBeInTheDocument();
    expect(screen.getByText(/\/Materials\/Documents/)).toBeInTheDocument();
  });

  it('shows multiple-matches warning', () => {
    render(
      <FolderStatusBanner status={FolderStatus.MultipleMatches} expectedPrefix="MAT001__" basePath="/Materials/Documents" />
    );
    expect(screen.getByText(/více složek/i)).toBeInTheDocument();
  });
});
```

Only the `status` prop values changed (`FolderStatus.Found` / `FolderStatus.NotFound` / `FolderStatus.MultipleMatches` instead of the plain string literals) plus the new import. Assertions are unchanged.

- [ ] **Step 10: Run the full TypeScript check again — confirm it is clean**

Run:
```bash
cd frontend
npx tsc --noEmit -p tsconfig.json
```
Expected: no errors.

- [ ] **Step 11: Run the affected test suites — confirm they pass**

Run:
```bash
cd frontend
CI=true npx react-scripts test --testPathPattern="catalog/detail" --watchAll=false
```
Expected: all suites PASS — `DocumentList.test.tsx`, `FolderStatusBanner.test.tsx`, and `MaterialUploadDialog.test.tsx` (the latter unmodified, confirming FR-4/NFR-1's "no consumer test changes needed" holds for that file specifically).

- [ ] **Step 12: Full frontend build**

Run:
```bash
cd frontend
npm run build
```
Expected: builds successfully with no new TypeScript errors or warnings attributable to this change.

- [ ] **Step 13: Lint**

Run:
```bash
cd frontend
npm run lint
```
Expected: passes with no new violations (in particular, no `(apiClient as any)` / `no-explicit-any`-style findings remain in `useCatalogDocuments.ts`).

- [ ] **Step 14: Full frontend test suite (regression check)**

Run:
```bash
cd frontend
CI=true npx react-scripts test --watchAll=false
```
Expected: all tests PASS — no regressions outside the catalog-documents area (this file/its types are not referenced anywhere else, per the repo-wide grep performed during planning).

- [ ] **Step 15: Commit**

```bash
cd frontend
git add src/api/hooks/useCatalogDocuments.ts \
        src/components/catalog/detail/tabs/shared/DocumentList.tsx \
        src/components/catalog/detail/tabs/shared/FolderStatusBanner.tsx \
        src/components/catalog/detail/tabs/MaterialDocumentsTab.tsx \
        src/components/catalog/detail/tabs/PifDocumentsTab.tsx \
        src/components/catalog/detail/tabs/shared/__tests__/DocumentList.test.tsx \
        src/components/catalog/detail/tabs/shared/__tests__/FolderStatusBanner.test.tsx
git commit -m "refactor(catalog-documents): use generated NSwag client instead of private-field reach-around"
```

#### Acceptance criteria
- All acceptance criteria in `spec.r1.md` FR-1, FR-2, FR-3, FR-4, NFR-1, NFR-2, NFR-3 are met.
- No hook in `useCatalogDocuments.ts` calls `apiFetch`, `(apiClient as any)`, or any private field of `ApiClient` (FR-1).
- All five hooks' `queryKey`/`staleTime`/`enabled`/`retry`/`onSuccess` invalidation behavior is byte-for-byte unchanged (FR-1, FR-2, NFR-2).
- `useCatalogDocuments.ts` contains no local `interface`/`type` declaration duplicating a generated type's shape (FR-3).
- `npm run build` and `npm run lint` both pass with no new errors/warnings (FR-4).
- `MaterialUploadDialog.tsx`, `PifUploadDialog.tsx`, `MaterialUploadDialog.test.tsx` require no source changes, confirmed by running them unmodified (FR-4, NFR-1).
- `DocumentList.tsx`, `FolderStatusBanner.tsx`, and their tests — not named in the spec's consumer list but structurally dependent on the deleted types — are updated and pass, closing the gap between the spec's stated blast radius and the actual one.
