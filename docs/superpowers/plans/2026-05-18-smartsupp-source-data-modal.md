# Smartsupp: "Zdroj dat" Source Data Modal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hover tooltip in `DraftReplyToolbar` with a "Zdroj dat" button that opens a browsable modal of knowledge-base source records, each clickable to show the full chunk via `ChunkDetailModal`.

**Architecture:** Add `ChunkId` to the backend `DraftReplySource` DTO and map it from `ChunkResult`; propagate it to the frontend `DraftReplySource` interface; build a new `DraftReplySourcesModal` component that lists sources and stacks `ChunkDetailModal` on top when a row is selected; replace the toolbar's `Info` tooltip with a `Database`-icon button that opens the modal.

**Tech Stack:** .NET 8 (C#) backend with xUnit + FluentAssertions + Moq; React 18 + TypeScript frontend with React Testing Library + Jest.

---

## File Map

| Status | File | Change |
|--------|------|--------|
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs` | Add `ChunkId` to `DraftReplySource` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` | Map `ChunkId = c.ChunkId` in Sources projection |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` | Assert `Sources[0].ChunkId` in the mapped-sources test |
| Modify | `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts` | Add `chunkId: string` to `DraftReplySource` interface |
| Modify | `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx` | Update fixture + replace tooltip test with modal test |
| Create | `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplySourcesModal.test.tsx` | New component tests |
| Create | `frontend/src/components/customer-support/smartsupp/DraftReplySourcesModal.tsx` | New modal component |
| Modify | `frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx` | Replace `Info` tooltip with `Database` "Zdroj dat" button and modal |

---

### Task 1: BE — Add `ChunkId` to `DraftReplySource` and map it from `ChunkResult`

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`

- [ ] **Step 1: Update `Handle_ReturnsAnswerAndMappedSources_OnSuccess` to assert `ChunkId` is mapped**

In `GenerateDraftReplyHandlerTests.cs`, replace the existing `Handle_ReturnsAnswerAndMappedSources_OnSuccess` test (the one at ~line 157) with:

```csharp
[Fact]
public async Task Handle_ReturnsAnswerAndMappedSources_OnSuccess()
{
    var chunk = Chunk("Obsah dokumentu o dopravě.", "doprava.pdf");
    SetupConversation(ConversationWith(
        Msg("m1", SmartsuppMessageAuthorType.Visitor, "Dotaz", 1)));
    SetupSearch(chunk);
    SetupChat("Dobrý den, balíky odesíláme do 24 hodin.");

    var result = await CreateHandler().Handle(
        new GenerateDraftReplyRequest { ConversationId = "c1", Topic = "Doprava" }, CancellationToken.None);

    result.Success.Should().BeTrue();
    result.Answer.Should().Be("Dobrý den, balíky odesíláme do 24 hodin.");
    result.Sources.Should().ContainSingle();
    result.Sources[0].Filename.Should().Be("doprava.pdf");
    result.Sources[0].Excerpt.Should().Be("Obsah dokumentu o dopravě.");
    result.Sources[0].ChunkId.Should().Be(chunk.ChunkId);
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~Handle_ReturnsAnswerAndMappedSources_OnSuccess" 2>&1 | tail -20
```

Expected: compilation error — `'DraftReplySource' does not contain a definition for 'ChunkId'`.

- [ ] **Step 3: Add `ChunkId` to `DraftReplySource`**

In `GenerateDraftReplyResponse.cs`, replace the `DraftReplySource` class (lines 15–21):

```csharp
public class DraftReplySource
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public double Score { get; set; }
}
```

- [ ] **Step 4: Map `ChunkId` in the handler**

In `GenerateDraftReplyHandler.cs`, replace the `Sources` projection (starting at `Sources = searchResult.Chunks.Select(…`):

```csharp
Sources = searchResult.Chunks.Select(c => new DraftReplySource
{
    ChunkId = c.ChunkId,
    DocumentId = c.DocumentId,
    Filename = c.SourceFilename,
    Excerpt = c.Content[..Math.Min(MaxExcerptLength, c.Content.Length)],
    Score = c.Score,
}).ToList(),
```

- [ ] **Step 5: Run all tests in `GenerateDraftReplyHandlerTests` to confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GenerateDraftReplyHandlerTests" 2>&1 | tail -20
```

Expected: all tests pass (green).

- [ ] **Step 6: Build and format**

```bash
dotnet build backend/ 2>&1 | tail -10
dotnet format backend/ 2>&1 | tail -5
```

Expected: `Build succeeded`, no format changes reported.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyResponse.cs \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs
git commit -m "feat(smartsupp): propagate ChunkId through DraftReplySource"
```

---

### Task 2: FE Hook — Add `chunkId` to `DraftReplySource` and update existing test fixture

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts`
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx`

- [ ] **Step 1: Add `chunkId` to `DraftReplySource` interface**

In `useGenerateDraftReply.ts`, replace the `DraftReplySource` interface (lines 4–10):

```typescript
export interface DraftReplySource {
  chunkId: string;
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}
```

- [ ] **Step 2: Run TypeScript build to see the error cascade**

```bash
cd frontend && npm run build 2>&1 | grep "error TS" | head -20
```

Expected: TypeScript errors referencing `DraftReplySource` objects missing `chunkId` — at minimum in `DraftReplyToolbar.test.tsx`.

- [ ] **Step 3: Update the test fixture in `DraftReplyToolbar.test.tsx`**

In `DraftReplyToolbar.test.tsx`, replace the `sources` constant (lines 6–8):

```typescript
const sources: DraftReplySource[] = [
  { chunkId: 'chunk-1', documentId: 'd1', filename: 'reklamace.pdf', excerpt: '...', score: 0.9 },
];
```

- [ ] **Step 4: Run build to confirm no TypeScript errors**

```bash
cd frontend && npm run build 2>&1 | tail -5
```

Expected: `Compiled successfully.`

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/hooks/useGenerateDraftReply.ts \
        frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx
git commit -m "feat(smartsupp): add chunkId to DraftReplySource interface"
```

---

### Task 3: FE — Create `DraftReplySourcesModal` (TDD)

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplySourcesModal.test.tsx`
- Create: `frontend/src/components/customer-support/smartsupp/DraftReplySourcesModal.tsx`

- [ ] **Step 1: Write the failing tests**

Create `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplySourcesModal.test.tsx`:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import DraftReplySourcesModal from '../DraftReplySourcesModal';
import type { DraftReplySource } from '../hooks/useGenerateDraftReply';

jest.mock('../../../knowledge-base/ChunkDetailModal', () => ({
  __esModule: true,
  default: ({ chunkId, onClose }: { chunkId: string; onClose: () => void }) => (
    <div data-testid="chunk-detail-modal" data-chunk-id={chunkId}>
      <button onClick={onClose}>Zavřít detail</button>
    </div>
  ),
}));

const sources: DraftReplySource[] = [
  {
    chunkId: 'chunk-1',
    documentId: 'd1',
    filename: 'reklamace.pdf',
    excerpt: 'Reklamaci lze uplatnit do 14 dnů.',
    score: 0.9,
  },
  {
    chunkId: 'chunk-2',
    documentId: 'd2',
    filename: 'doprava.pdf',
    excerpt: 'Dopravujeme do 24 hodin.',
    score: 0.8,
  },
];

describe('DraftReplySourcesModal', () => {
  it('renders all source filenames', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    expect(screen.getByText('reklamace.pdf')).toBeInTheDocument();
    expect(screen.getByText('doprava.pdf')).toBeInTheDocument();
  });

  it('renders score as percentage for each source', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    expect(screen.getByText('90%')).toBeInTheDocument();
    expect(screen.getByText('80%')).toBeInTheDocument();
  });

  it('renders excerpt text for each source', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    expect(screen.getByText('Reklamaci lze uplatnit do 14 dnů.')).toBeInTheDocument();
    expect(screen.getByText('Dopravujeme do 24 hodin.')).toBeInTheDocument();
  });

  it('opens ChunkDetailModal with correct chunkId when a source row is clicked', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    fireEvent.click(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
    );
    const detail = screen.getByTestId('chunk-detail-modal');
    expect(detail).toBeInTheDocument();
    expect(detail.dataset.chunkId).toBe('chunk-1');
  });

  it('returns to source list when ChunkDetailModal onClose is called', () => {
    render(<DraftReplySourcesModal sources={sources} onClose={jest.fn()} />);
    fireEvent.click(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
    );
    fireEvent.click(screen.getByText('Zavřít detail'));
    expect(screen.getByText('reklamace.pdf')).toBeInTheDocument();
    expect(screen.queryByTestId('chunk-detail-modal')).not.toBeInTheDocument();
  });

  it('calls onClose when the X button is clicked', () => {
    const onClose = jest.fn();
    render(<DraftReplySourcesModal sources={sources} onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: /zavřít/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onClose when Escape key is pressed on the source list', () => {
    const onClose = jest.fn();
    render(<DraftReplySourcesModal sources={sources} onClose={onClose} />);
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('does not call onClose on Escape when ChunkDetailModal is open', () => {
    const onClose = jest.fn();
    render(<DraftReplySourcesModal sources={sources} onClose={onClose} />);
    fireEvent.click(
      screen.getByRole('button', { name: /zobrazit zdroj reklamace\.pdf/i }),
    );
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="DraftReplySourcesModal" 2>&1 | tail -20
```

Expected: `Cannot find module '../DraftReplySourcesModal'` — component doesn't exist yet.

- [ ] **Step 3: Create `DraftReplySourcesModal.tsx`**

Create `frontend/src/components/customer-support/smartsupp/DraftReplySourcesModal.tsx`:

```typescript
import { useState, useEffect } from 'react';
import { X } from 'lucide-react';
import type { DraftReplySource } from './hooks/useGenerateDraftReply';
import ChunkDetailModal from '../../knowledge-base/ChunkDetailModal';

interface DraftReplySourcesModalProps {
  sources: DraftReplySource[];
  onClose: () => void;
}

function DraftReplySourcesModal({ sources, onClose }: DraftReplySourcesModalProps) {
  const [selectedSource, setSelectedSource] = useState<DraftReplySource | null>(null);

  useEffect(() => {
    if (selectedSource) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose, selectedSource]);

  return (
    <>
      <div
        role="dialog"
        aria-modal="true"
        className="fixed inset-0 bg-black/50 flex items-center justify-center z-40"
      >
        <div className="bg-white rounded-lg shadow-xl w-[600px] max-h-[80vh] overflow-hidden flex flex-col">
          <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 flex-shrink-0">
            <h2 className="text-base font-semibold text-gray-800">Zdroj dat</h2>
            <button
              onClick={onClose}
              className="p-1 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100"
              aria-label="Zavřít"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
          <div className="flex-1 overflow-y-auto divide-y divide-gray-100">
            {sources.map((source, index) => (
              <div
                key={`${source.chunkId}-${index}`}
                className="px-6 py-4 space-y-1 cursor-pointer hover:bg-gray-50"
                onClick={() => setSelectedSource(source)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => e.key === 'Enter' && setSelectedSource(source)}
                aria-label={`Zobrazit zdroj ${source.filename}`}
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm font-medium text-gray-700">{source.filename}</span>
                  <span className="text-xs px-1.5 py-0.5 rounded font-medium bg-gray-100 text-gray-600">
                    {Math.round(source.score * 100)}%
                  </span>
                </div>
                <p className="text-xs text-gray-500 italic line-clamp-3">{source.excerpt}</p>
              </div>
            ))}
          </div>
        </div>
      </div>
      {selectedSource && (
        <ChunkDetailModal
          chunkId={selectedSource.chunkId}
          score={selectedSource.score}
          onClose={() => setSelectedSource(null)}
        />
      )}
    </>
  );
}

export default DraftReplySourcesModal;
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="DraftReplySourcesModal" 2>&1 | tail -20
```

Expected: all 8 tests pass (green).

- [ ] **Step 5: Run build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -5 && npm run lint 2>&1 | tail -5
```

Expected: `Compiled successfully.`, no lint errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/DraftReplySourcesModal.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/DraftReplySourcesModal.test.tsx
git commit -m "feat(smartsupp): add DraftReplySourcesModal component"
```

---

### Task 4: FE — Replace `Info` tooltip with "Zdroj dat" modal button in `DraftReplyToolbar`

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx`

- [ ] **Step 1: Replace toolbar tests**

Replace the full content of `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx`:

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import DraftReplyToolbar from '../DraftReplyToolbar';
import type { DraftReplySource } from '../hooks/useGenerateDraftReply';

const sources: DraftReplySource[] = [
  { chunkId: 'chunk-1', documentId: 'd1', filename: 'reklamace.pdf', excerpt: '...', score: 0.9 },
];

describe('DraftReplyToolbar', () => {
  it('calls onRegenerate when the regenerate button is clicked', () => {
    const onRegenerate = jest.fn();
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={onRegenerate} onDiscard={jest.fn()} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /regenerovat/i }));
    expect(onRegenerate).toHaveBeenCalledTimes(1);
  });

  it('calls onDiscard when the discard button is clicked', () => {
    const onDiscard = jest.fn();
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={jest.fn()} onDiscard={onDiscard} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /zahodit/i }));
    expect(onDiscard).toHaveBeenCalledTimes(1);
  });

  it('opens DraftReplySourcesModal when "Zdroj dat" is clicked', () => {
    render(
      <DraftReplyToolbar sources={sources} onRegenerate={jest.fn()} onDiscard={jest.fn()} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /zdroj dat/i }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('does not render the "Zdroj dat" button when there are no sources', () => {
    render(
      <DraftReplyToolbar sources={[]} onRegenerate={jest.fn()} onDiscard={jest.fn()} />,
    );
    expect(screen.queryByRole('button', { name: /zdroj dat/i })).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests to confirm the new tests fail**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="DraftReplyToolbar\.test" 2>&1 | tail -20
```

Expected: `opens DraftReplySourcesModal when "Zdroj dat" is clicked` fails (button not found); `does not render the "Zdroj dat" button when there are no sources` also fails (old `Zdroje` button still present).

- [ ] **Step 3: Replace `DraftReplyToolbar.tsx`**

Replace the full content of `frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx`:

```typescript
import { useState } from 'react';
import { RefreshCw, X, Database } from 'lucide-react';
import type { DraftReplySource } from './hooks/useGenerateDraftReply';
import DraftReplySourcesModal from './DraftReplySourcesModal';

interface DraftReplyToolbarProps {
  sources: DraftReplySource[];
  disabled?: boolean;
  onRegenerate: () => void;
  onDiscard: () => void;
}

function DraftReplyToolbar({ sources, disabled, onRegenerate, onDiscard }: DraftReplyToolbarProps) {
  const [showSourcesModal, setShowSourcesModal] = useState(false);

  return (
    <>
      <div className="flex items-center gap-3 text-xs">
        <span className="font-medium text-blue-600">Návrh od AI</span>
        <button
          type="button"
          disabled={disabled}
          onClick={onRegenerate}
          className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <RefreshCw className="w-3.5 h-3.5" />
          Regenerovat
        </button>
        <button
          type="button"
          onClick={onDiscard}
          className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900"
        >
          <X className="w-3.5 h-3.5" />
          Zahodit
        </button>
        {sources.length > 0 && (
          <button
            type="button"
            onClick={() => setShowSourcesModal(true)}
            className="inline-flex items-center gap-1 text-gray-600 hover:text-gray-900"
          >
            <Database className="w-3.5 h-3.5" />
            Zdroj dat
          </button>
        )}
      </div>
      {showSourcesModal && (
        <DraftReplySourcesModal
          sources={sources}
          onClose={() => setShowSourcesModal(false)}
        />
      )}
    </>
  );
}

export default DraftReplyToolbar;
```

- [ ] **Step 4: Run all toolbar and sources modal tests to confirm they pass**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="DraftReplyToolbar|DraftReplySourcesModal" 2>&1 | tail -20
```

Expected: all 12 tests pass (4 toolbar + 8 sources modal).

- [ ] **Step 5: Final build and lint**

```bash
cd frontend && npm run build 2>&1 | tail -5 && npm run lint 2>&1 | tail -5
```

Expected: `Compiled successfully.`, no lint errors.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/DraftReplyToolbar.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyToolbar.test.tsx
git commit -m "feat(smartsupp): replace tooltip with Zdroj dat modal button"
```
