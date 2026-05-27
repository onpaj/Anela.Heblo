# Smartsupp Per-Chat Draft Retention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve each Smartsupp conversation's composer draft independently so typing in Chat A does not bleed into Chat B when switching conversations.

**Architecture:** Store drafts in a `Record<string, string>` in `SmartsuppChatsPage`. Add `key={conversationId}` to `ChatComposer` (via `ConversationDetail`) so React remounts it on every switch. Pass `initialDraft` and `onDraftChange` through `ConversationDetail` into `ChatComposer` to hydrate and persist drafts. AI-draft metadata resets on switch — only the text is preserved (YAGNI).

**Tech Stack:** React 18, TypeScript, Vitest/Jest + React Testing Library

---

## File Map

| File | Change |
|------|--------|
| `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx` | Add `initialDraft?: string` and `onDraftChange?: (draft: string) => void` props; wire into state init and change handlers |
| `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx` | Add 4 tests for `initialDraft` and `onDraftChange` behavior |
| `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx` | Add `initialDraft?: string` and `onDraftChange?: (draft: string) => void` to props; add `key={conversationId}` to `<ChatComposer>`; pass props through |
| `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx` | Add 1 test that `initialDraft` pre-fills the textarea |
| `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx` | Add `drafts` state + `handleDraftChange`; pass `initialDraft` and `onDraftChange` to `<ConversationDetail>` |
| `frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx` | Add 1 integration test for draft persistence across conversation switches |

---

## Task 1: ChatComposer — add initialDraft and onDraftChange props

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ChatComposer.tsx`

- [ ] **Step 1: Write 4 failing tests**

Open `frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx`.

Add these 4 tests inside the existing `describe("ChatComposer", ...)` block, after the last existing `it(...)`:

```tsx
  it("initializes textarea with initialDraft value", () => {
    mockHook({});
    render(<ChatComposer conversationId="c1" lastContactMessage={null} initialDraft="Předvyplněný draft" />);
    const textarea = screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement;
    expect(textarea.value).toBe("Předvyplněný draft");
  });

  it("calls onDraftChange when user types", () => {
    mockHook({});
    const onDraftChange = jest.fn();
    render(<ChatComposer conversationId="c1" lastContactMessage={null} onDraftChange={onDraftChange} />);
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), { target: { value: "Ahoj" } });
    expect(onDraftChange).toHaveBeenCalledWith("Ahoj");
  });

  it("calls onDraftChange with empty string on discard", async () => {
    const onDraftChange = jest.fn();
    mockHook({ result: { answer: "Vygenerovaná odpověď", sources: [] } });
    render(<ChatComposer conversationId="c1" lastContactMessage="Hi" onDraftChange={onDraftChange} />);
    expect(await screen.findByText("Návrh od AI")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /zahodit/i }));
    expect(onDraftChange).toHaveBeenLastCalledWith("");
  });

  it("calls onDraftChange with AI answer when draft is generated", async () => {
    const onDraftChange = jest.fn();
    mockHook({ result: { answer: "AI odpověď", sources: [] } });
    render(<ChatComposer conversationId="c1" lastContactMessage="Hi" onDraftChange={onDraftChange} />);
    await waitFor(() => expect(onDraftChange).toHaveBeenCalledWith("AI odpověď"));
  });
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="ChatComposer.test"
```

Expected: 4 new tests FAIL. The existing tests pass (no regressions).

- [ ] **Step 3: Implement the changes in ChatComposer.tsx**

Replace the entire file with:

```tsx
import { useEffect, useState } from "react";
import { Send } from "lucide-react";
import DraftReplyTriggerBar from "./DraftReplyTriggerBar";
import DraftReplyToolbar from "./DraftReplyToolbar";
import { useGenerateDraftReply, type DraftReplySource } from "./hooks/useGenerateDraftReply";

interface ChatComposerProps {
  conversationId: string | null;
  lastContactMessage: string | null;
  initialDraft?: string;
  onDraftChange?: (draft: string) => void;
}

const MAX_CHARS = 4000;

function ChatComposer({
  conversationId,
  lastContactMessage,
  initialDraft,
  onDraftChange,
}: ChatComposerProps) {
  const [draft, setDraft] = useState(initialDraft ?? "");
  const [isAiDraft, setIsAiDraft] = useState(false);
  const [sources, setSources] = useState<DraftReplySource[]>([]);
  const [lastTopic, setLastTopic] = useState<string | undefined>(undefined);
  const [pendingTopic, setPendingTopic] = useState<{ topic: string | undefined } | null>(null);

  const { generate, isLoading, error, result, reset } = useGenerateDraftReply(conversationId);

  // Move a freshly generated answer into the composer as an editable AI draft.
  useEffect(() => {
    if (result) {
      const answer = result.answer.slice(0, MAX_CHARS);
      setDraft(answer);
      setSources(result.sources);
      setIsAiDraft(true);
      onDraftChange?.(answer);
      reset();
    }
  }, [result, reset, onDraftChange]);

  const canGenerateWithoutTopic =
    lastContactMessage !== null && lastContactMessage.trim() !== "";

  const requestGeneration = (topic?: string) => {
    if (draft.trim() !== "" && !isAiDraft) {
      setPendingTopic({ topic });
      return;
    }
    setLastTopic(topic);
    generate(topic);
  };

  const confirmOverwrite = () => {
    if (pendingTopic === null) return;
    setLastTopic(pendingTopic.topic);
    generate(pendingTopic.topic);
    setPendingTopic(null);
  };

  const cancelOverwrite = () => setPendingTopic(null);

  const handleDraftChange = (value: string) => {
    const trimmed = value.slice(0, MAX_CHARS);
    setDraft(trimmed);
    onDraftChange?.(trimmed);
    if (isAiDraft) {
      setIsAiDraft(false);
    }
  };

  const handleDiscard = () => {
    setDraft("");
    setSources([]);
    setIsAiDraft(false);
    setLastTopic(undefined);
    setPendingTopic(null);
    onDraftChange?.("");
  };

  return (
    <div className="flex flex-col">
      <DraftReplyTriggerBar
        disabled={isLoading}
        canGenerateWithoutTopic={canGenerateWithoutTopic}
        error={error}
        onGenerate={requestGeneration}
      />
      {pendingTopic !== null && (
        <div className="flex items-center justify-between border-t border-amber-200 bg-amber-50 px-4 py-2 text-xs">
          <span className="text-amber-800">Přepsat rozepsanou odpověď?</span>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={confirmOverwrite}
              className="font-medium text-amber-800 hover:text-amber-900"
            >
              Přepsat
            </button>
            <button
              type="button"
              onClick={cancelOverwrite}
              className="text-gray-500 hover:text-gray-700"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}
      <div className="flex flex-col gap-2 border-t border-gray-200 bg-white p-3">
        {isAiDraft && (
          <DraftReplyToolbar
            sources={sources}
            disabled={isLoading}
            onRegenerate={() => generate(lastTopic)}
            onDiscard={handleDiscard}
          />
        )}
        <textarea
          value={draft}
          disabled={isLoading}
          onChange={(e) => handleDraftChange(e.target.value)}
          placeholder={isLoading ? "Generuji návrh odpovědi…" : "Napište odpověď..."}
          rows={3}
          className="w-full resize-none rounded-md border border-gray-200 px-3 py-2 text-sm focus:border-blue-400 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-50"
        />
        <div className="flex items-center justify-between">
          <span className="text-xs text-gray-400">
            {draft.length} / {MAX_CHARS}
          </span>
          <button
            type="button"
            disabled
            title="Odpovídání bude přidáno později"
            aria-label="Odeslat"
            className="inline-flex cursor-not-allowed items-center gap-2 rounded-md bg-blue-500 px-3 py-1.5 text-sm font-medium text-white opacity-50"
          >
            <Send className="h-4 w-4" />
            Odeslat
          </button>
        </div>
      </div>
    </div>
  );
}

export default ChatComposer;
```

- [ ] **Step 4: Run tests to verify all pass**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="ChatComposer.test"
```

Expected: All tests PASS (4 new + 7 existing = 11 total).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ChatComposer.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/ChatComposer.test.tsx
git commit -m "feat(smartsupp): add initialDraft and onDraftChange props to ChatComposer"
```

---

## Task 2: ConversationDetail — thread props through and add key

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`

- [ ] **Step 1: Write a failing test**

Open `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx`.

Add this test inside the existing `describe("ConversationDetail", ...)` block, after the last existing `it(...)`:

```tsx
  it("pre-fills the composer textarea with initialDraft", () => {
    render(
      wrap(
        <ConversationDetail
          conversationId="c1"
          conversation={conv}
          initialDraft="Předvyplněný text"
          onDraftChange={jest.fn()}
        />,
      ),
    );
    const textarea = screen.getByPlaceholderText("Napište odpověď...") as HTMLTextAreaElement;
    expect(textarea.value).toBe("Předvyplněný text");
  });
```

- [ ] **Step 2: Run to verify it fails**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="ConversationDetail.test"
```

Expected: The new test FAILS (TypeScript error or assertion failure). Existing 5 tests pass.

- [ ] **Step 3: Implement the changes in ConversationDetail.tsx**

Replace the entire file with:

```tsx
import React, { useEffect, useRef } from "react";
import { ConversationDto, MessageDto, useSmartsuppConversation } from "../../../api/hooks/useSmartsupp";
import MessageBubble from "./MessageBubble";
import StatusPill from "./StatusPill";
import AgentBadge from "./AgentBadge";
import DaySeparator from "./DaySeparator";
import ChatComposer from "./ChatComposer";

interface ConversationDetailProps {
  conversationId: string;
  conversation: ConversationDto;
  initialDraft?: string;
  onDraftChange?: (draft: string) => void;
}

// Returns the most recent customer message that actually carries text.
// SmartSupp emits page-visit events as authorType "Visitor"/subType "system"
// with empty content — those are skipped so they don't mask the real message.
export function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const authorType = m.authorType.toLowerCase();
    const isContact = authorType === "visitor" || authorType === "contact";
    const isSystem =
      authorType === "system" || (m.subType ?? "").toLowerCase() === "system";
    if (isContact && !isSystem) {
      const content = m.content?.trim();
      if (content) return content;
    }
  }
  return null;
}

function groupByDay(messages: MessageDto[]): Array<{ day: string; items: MessageDto[] }> {
  const groups: Array<{ day: string; items: MessageDto[] }> = [];
  for (const m of messages) {
    const day = new Date(m.createdAt).toISOString().slice(0, 10);
    const last = groups[groups.length - 1];
    if (last && last.day === day) {
      last.items.push(m);
    } else {
      groups.push({ day, items: [m] });
    }
  }
  return groups;
}

const ConversationDetail: React.FC<ConversationDetailProps> = ({
  conversationId,
  conversation,
  initialDraft,
  onDraftChange,
}) => {
  const { data, isLoading } = useSmartsuppConversation(conversationId);
  const bottomRef = useRef<HTMLDivElement>(null);
  const messages = data?.messages ?? [];

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.length, conversationId]);

  const displayName = conversation.contactName ?? conversation.contactEmail ?? "Neznámý";
  const grouped = groupByDay(messages);

  return (
    <div className="flex flex-col h-full">
      <div className="px-6 py-3 border-b border-gray-200 flex items-center gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-gray-900 truncate">{displayName}</h3>
            <StatusPill status={conversation.status} />
          </div>
          {conversation.contactEmail && (
            <p className="text-xs text-gray-500 truncate">{conversation.contactEmail}</p>
          )}
        </div>
        <div className="ml-auto flex items-center gap-2">
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={id} />
          ))}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-6 py-4">
        {isLoading && (
          <div className="text-sm text-gray-400 text-center py-8">Načítání zpráv...</div>
        )}
        {!isLoading && messages.length === 0 && (
          <div className="text-sm text-gray-400 text-center py-8">Žádné zprávy</div>
        )}
        {grouped.map((g) => (
          <div key={g.day}>
            <DaySeparator date={g.items[0].createdAt} />
            {g.items.map((m) => (
              <MessageBubble key={m.id} message={m} />
            ))}
          </div>
        ))}
        <div ref={bottomRef} />
      </div>

      <ChatComposer
        key={conversationId}
        conversationId={conversationId}
        lastContactMessage={lastContactMessage(messages)}
        initialDraft={initialDraft}
        onDraftChange={onDraftChange}
      />
    </div>
  );
};

export default ConversationDetail;
```

- [ ] **Step 4: Run tests to verify all pass**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="ConversationDetail.test"
```

Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx
git commit -m "feat(smartsupp): thread initialDraft/onDraftChange through ConversationDetail, add key"
```

---

## Task 3: SmartsuppChatsPage — add draft state and pass props

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`

- [ ] **Step 1: Write a failing integration test**

Open `frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx`.

Add a second mock conversation object after `mockConversationItem`:

```tsx
const mockConversationItem2 = {
  ...mockConversationItem,
  id: "c2",
  contactName: "Pavel Novák",
  contactEmail: "pavel@example.com",
};
```

Update the `useSmartsuppConversations` return value in the `jest.mock` call to return both items:

```tsx
jest.mock("../../../../../api/hooks/useSmartsupp", () => ({
  useSmartsuppConversations: () => ({
    data: {
      success: true,
      items: [mockConversationItem, mockConversationItem2],
      total: 2,
      page: 1,
      pageSize: 100,
    },
    isLoading: false,
  }),
  useSmartsuppConversation: () => ({ data: { messages: [] }, isLoading: false }),
  useTriggerSmartsuppSync: () => ({ mutate: jest.fn(), isPending: false }),
  SMARTSUPP_QUERY_KEYS: { conversations: () => [], conversation: () => [] },
}));
```

**Note:** The `mockConversationItem2` reference is used inside `jest.mock` which is hoisted by Babel/Jest before variable declarations. To avoid a "cannot access before initialization" error, inline the second item directly instead of referencing the variable:

Replace the entire `jest.mock` block (including the `mockConversationItem` const) with this pattern that places the inline objects inside the mock factory:

```tsx
const BASE_CONV = {
  subject: null,
  contactAvatarUrl: null,
  status: "open" as const,
  isUnread: false,
  lastMessageAt: new Date().toISOString(),
  lastMessagePreview: null,
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
  assignedAgentIds: [] as string[],
  isServed: true,
  tags: [] as string[],
};

const mockConversationItem = {
  ...BASE_CONV,
  id: "c1",
  contactName: "Jana Nováková",
  contactEmail: "jana@example.com",
};

const mockConversationItem2 = {
  ...BASE_CONV,
  id: "c2",
  contactName: "Pavel Novák",
  contactEmail: "pavel@example.com",
};

jest.mock("../../../../../api/hooks/useSmartsupp", () => ({
  useSmartsuppConversations: () => ({
    data: {
      success: true,
      items: [
        {
          id: "c1",
          subject: null,
          contactName: "Jana Nováková",
          contactEmail: "jana@example.com",
          contactAvatarUrl: null,
          status: "open",
          isUnread: false,
          lastMessageAt: new Date().toISOString(),
          lastMessagePreview: null,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          assignedAgentIds: [],
          isServed: true,
          tags: [],
        },
        {
          id: "c2",
          subject: null,
          contactName: "Pavel Novák",
          contactEmail: "pavel@example.com",
          contactAvatarUrl: null,
          status: "open",
          isUnread: false,
          lastMessageAt: new Date().toISOString(),
          lastMessagePreview: null,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          assignedAgentIds: [],
          isServed: true,
          tags: [],
        },
      ],
      total: 2,
      page: 1,
      pageSize: 100,
    },
    isLoading: false,
  }),
  useSmartsuppConversation: () => ({ data: { messages: [] }, isLoading: false }),
  useTriggerSmartsuppSync: () => ({ mutate: jest.fn(), isPending: false }),
  SMARTSUPP_QUERY_KEYS: { conversations: () => [], conversation: () => [] },
}));
```

Then add the draft persistence test inside `describe("SmartsuppChatsPage", ...)`, after the last existing `it(...)`:

```tsx
  it("preserves draft text when switching between conversations", () => {
    render(wrap(<SmartsuppChatsPage />));

    // Select first conversation and type a draft
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    fireEvent.change(screen.getByPlaceholderText(/napište odpověď/i), {
      target: { value: "draft A" },
    });

    // Switch to second conversation — textarea should be empty
    fireEvent.click(screen.getByRole("button", { name: /Pavel Novák/ }));
    expect(
      (screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement).value,
    ).toBe("");

    // Switch back to first — draft must be restored
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    expect(
      (screen.getByPlaceholderText(/napište odpověď/i) as HTMLTextAreaElement).value,
    ).toBe("draft A");
  });
```

- [ ] **Step 2: Run to verify the new test fails**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="SmartsuppChatsPage.test"
```

Expected: The new "preserves draft text" test FAILS. Existing tests pass.

- [ ] **Step 3: Implement draft state in SmartsuppChatsPage.tsx**

Replace the entire file with:

```tsx
import React, { useState } from "react";
import { useSmartsuppConversations, useTriggerSmartsuppSync } from "../../../../api/hooks/useSmartsupp";
import { useToast } from "../../../../contexts/ToastContext";
import ConversationList from "../ConversationList";
import ConversationDetail from "../ConversationDetail";
import ContactDetailsPanel from "../ContactDetailsPanel";
import CollapsibleRail from "../CollapsibleRail";

const LIST_PANEL_KEY = "smartsupp.listPanel.open";
const CONTACT_PANEL_KEY = "smartsupp.contactPanel.open";

function readPanelOpen(key: string, defaultOpen: boolean): boolean {
  if (typeof window === "undefined") return defaultOpen;
  const stored = window.localStorage.getItem(key);
  if (stored === null) return defaultOpen;
  return stored === "true";
}

const SmartsuppChatsPage: React.FC = () => {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState<"Open" | "Resolved">("Open");
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [listPanelOpen, setListPanelOpen] = useState<boolean>(() =>
    readPanelOpen(LIST_PANEL_KEY, true),
  );
  const [contactPanelOpen, setContactPanelOpen] = useState<boolean>(() =>
    readPanelOpen(CONTACT_PANEL_KEY, false),
  );

  const { data, isLoading } = useSmartsuppConversations(status);
  const { showSuccess, showError } = useToast();
  const syncMutation = useTriggerSmartsuppSync();

  const conversations = data?.items ?? [];
  const selectedConversation = conversations.find((c) => c.id === selectedId) ?? null;

  const handleDraftChange = (id: string, text: string) =>
    setDrafts((prev) => ({ ...prev, [id]: text }));

  const handleSyncClick = () => {
    syncMutation.mutate(undefined, {
      onSuccess: (result) => {
        showSuccess(
          "Synchronizace dokončena",
          `Konverzace: ${result.conversationsProcessed} • zprávy: ${result.messagesProcessed}`,
        );
      },
      onError: (error) => {
        showError("Synchronizace selhala", error instanceof Error ? error.message : "Neznámá chyba");
      },
    });
  };

  const togglePanel = (
    key: string,
    setter: React.Dispatch<React.SetStateAction<boolean>>,
  ) => {
    setter((open) => {
      const next = !open;
      window.localStorage.setItem(key, String(next));
      return next;
    });
  };

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <div className="flex items-center justify-end px-4 py-2 border-b border-gray-200 bg-white">
        <button
          type="button"
          onClick={handleSyncClick}
          disabled={syncMutation.isPending}
          className="inline-flex items-center gap-2 px-3 py-1.5 text-sm font-medium rounded-md border border-gray-300 bg-white hover:bg-gray-50 disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {syncMutation.isPending ? "Synchronizuji…" : "Sync now"}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden bg-white rounded-lg shadow-sm border border-gray-200">
        {listPanelOpen && (
          <div className="w-96 flex-shrink-0 overflow-hidden">
            <ConversationList
              conversations={conversations}
              selectedId={selectedId}
              status={status}
              isLoading={isLoading}
              onSelect={setSelectedId}
              onStatusChange={(s) => {
                setStatus(s);
                setSelectedId(null);
              }}
            />
          </div>
        )}
        <CollapsibleRail
          side="left"
          isOpen={listPanelOpen}
          label="Seznam konverzací"
          onToggle={() => togglePanel(LIST_PANEL_KEY, setListPanelOpen)}
        />

        <div className="flex-1 overflow-hidden min-w-0">
          {selectedConversation ? (
            <ConversationDetail
              conversationId={selectedId!}
              conversation={selectedConversation}
              initialDraft={drafts[selectedId!] ?? ""}
              onDraftChange={(text) => handleDraftChange(selectedId!, text)}
            />
          ) : (
            <div className="flex items-center justify-center h-full text-gray-400 text-sm">
              Vyberte konverzaci
            </div>
          )}
        </div>

        {selectedConversation && (
          <div className="hidden lg:flex">
            <CollapsibleRail
              side="right"
              isOpen={contactPanelOpen}
              label="Detail kontaktu"
              onToggle={() => togglePanel(CONTACT_PANEL_KEY, setContactPanelOpen)}
            />
            {contactPanelOpen && (
              <div className="w-80 flex-shrink-0 overflow-hidden">
                <ContactDetailsPanel conversation={selectedConversation} />
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartsuppChatsPage;
```

- [ ] **Step 4: Run all Smartsupp tests to verify everything passes**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="SmartsuppChatsPage.test"
```

Expected: All tests PASS including the new "preserves draft text" test.

- [ ] **Step 5: Run the full smartsupp test suite to catch any regressions**

```bash
cd frontend && react-scripts test --watchAll=false --testPathPattern="smartsupp"
```

Expected: All tests PASS (no regressions across the whole module).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx \
        frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx
git commit -m "feat(smartsupp): persist per-chat draft state across conversation switches"
```

---

## Task 4: Build and lint verification

**Files:** none (validation only)

- [ ] **Step 1: TypeScript build**

```bash
cd frontend && npm run build
```

Expected: Build exits with code 0. No TypeScript errors.

- [ ] **Step 2: Lint**

```bash
cd frontend && npm run lint
```

Expected: Lint exits with code 0. No ESLint errors or warnings.

---

## Self-Review

**Spec coverage:**
- ✅ Draft bleeds across chats → fixed by `key={conversationId}` on `ChatComposer`
- ✅ Each chat remembers its own draft → `drafts: Record<string, string>` in `SmartsuppChatsPage`
- ✅ Switching away and back restores draft text → tested in Task 3
- ✅ AI-draft metadata intentionally resets on switch (YAGNI) → component remounts fresh

**Placeholder scan:** None found. All steps contain complete code.

**Type consistency:**
- `ChatComposerProps.initialDraft?: string` — used as `useState(initialDraft ?? "")` ✅
- `ChatComposerProps.onDraftChange?: (draft: string) => void` — called as `onDraftChange?.(trimmed)` / `onDraftChange?.(answer)` / `onDraftChange?.("")` ✅
- `ConversationDetailProps.initialDraft?: string` — passed as `initialDraft={initialDraft}` to `ChatComposer` ✅
- `ConversationDetailProps.onDraftChange?: (draft: string) => void` — passed as `onDraftChange={onDraftChange}` ✅
- `handleDraftChange(id: string, text: string)` — called as `handleDraftChange(selectedId!, text)` ✅
- `drafts[selectedId!] ?? ""` — `selectedId!` is only evaluated when `selectedConversation` is truthy, so the non-null assertion is safe ✅
