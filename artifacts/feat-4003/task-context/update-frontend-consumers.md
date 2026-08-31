### task: update-frontend-consumers

**Files:**
- Modify: `frontend/src/components/pages/Journal/JournalList.tsx`
- Modify: `frontend/src/components/pages/CatalogDetail.tsx`
- Modify: `frontend/src/components/catalog/detail/CatalogDetailModals.tsx`
- Modify: `frontend/src/components/catalog/detail/tabs/JournalTab.tsx`
- Modify: `frontend/src/components/catalog/detail/CatalogDetailTabs.tsx`

- [ ] **Step 1: Update `JournalList.tsx`**

Change the import (currently):
```typescript
import type {
  JournalEntryDto,
  SearchJournalEntryDto,
} from "../../../api/generated/api-client";
```
to:
```typescript
import type {
  JournalEntryDto,
} from "../../../api/generated/api-client";
```

Then change the cast (currently):
```typescript
                {isSearchMode
                  ? (entries as SearchJournalEntryDto[]).map((entry) => (
```
to:
```typescript
                {isSearchMode
                  ? (entries as JournalEntryDto[]).map((entry) => (
```

- [ ] **Step 2: Update `CatalogDetail.tsx`**

Change:
```typescript
import { SearchJournalEntryDto } from "../../api/generated/api-client";
```
to:
```typescript
import { JournalEntryDto } from "../../api/generated/api-client";
```

Change:
```typescript
  const [selectedJournalEntry, setSelectedJournalEntry] = useState<
    SearchJournalEntryDto | undefined
  >(undefined);
```
to:
```typescript
  const [selectedJournalEntry, setSelectedJournalEntry] = useState<
    JournalEntryDto | undefined
  >(undefined);
```

- [ ] **Step 3: Update `CatalogDetailModals.tsx`**

Change:
```typescript
import { SearchJournalEntryDto } from "../../../api/generated/api-client";
```
to:
```typescript
import { JournalEntryDto } from "../../../api/generated/api-client";
```

Change:
```typescript
  selectedJournalEntry?: SearchJournalEntryDto;
```
to:
```typescript
  selectedJournalEntry?: JournalEntryDto;
```

- [ ] **Step 4: Update `JournalTab.tsx`**

Change:
```typescript
import type { SearchJournalEntryDto } from "../../../../api/generated/api-client";
```
to:
```typescript
import type { JournalEntryDto } from "../../../../api/generated/api-client";
```

Change:
```typescript
  onEditEntry: (entry: SearchJournalEntryDto) => void;
```
to:
```typescript
  onEditEntry: (entry: JournalEntryDto) => void;
```

- [ ] **Step 5: Update `CatalogDetailTabs.tsx`**

Change:
```typescript
import { SearchJournalEntryDto } from "../../../api/generated/api-client";
```
to:
```typescript
import { JournalEntryDto } from "../../../api/generated/api-client";
```

Change:
```typescript
  journalEntries: SearchJournalEntryDto[];
```
to:
```typescript
  journalEntries: JournalEntryDto[];
```

Change:
```typescript
  onEditJournalEntry: (entry: SearchJournalEntryDto) => void;
```
to:
```typescript
  onEditJournalEntry: (entry: JournalEntryDto) => void;
```

- [ ] **Step 6: Confirm no remaining references anywhere in the frontend**

Run: `grep -rn "SearchJournalEntryDto" frontend/src`
Expected: No output (no matches).

- [ ] **Step 7: Build the frontend**

Run: `cd frontend && npm run build`
Expected: Build succeeds with 0 TypeScript errors.

- [ ] **Step 8: Lint the frontend**

Run: `cd frontend && npm run lint`
Expected: Exits 0, no new lint errors introduced.

- [ ] **Step 9: Run frontend tests touching Journal/CatalogDetail**

Run: `cd frontend && npx jest src/components/pages/Journal src/components/pages/CatalogDetail.tsx src/components/catalog/detail --watchAll=false`
Expected: All tests PASS — `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` already types its mock data as `JournalEntryDto[]`, so no test-file changes are anticipated, but this step verifies that.

- [ ] **Step 10: Commit**

```bash
git add frontend/src/components/pages/Journal/JournalList.tsx \
        frontend/src/components/pages/CatalogDetail.tsx \
        frontend/src/components/catalog/detail/CatalogDetailModals.tsx \
        frontend/src/components/catalog/detail/tabs/JournalTab.tsx \
        frontend/src/components/catalog/detail/CatalogDetailTabs.tsx
git commit -m "refactor(journal): repoint frontend consumers from SearchJournalEntryDto to JournalEntryDto"
```

---

