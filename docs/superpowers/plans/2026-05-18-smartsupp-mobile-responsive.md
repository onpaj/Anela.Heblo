# Smartsupp Mobile-Responsive Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `/customer/smartsupp` fully usable on mobile phones (< 768 px) while leaving the existing desktop three-pane layout unchanged.

**Architecture:** One `mobileView` state (`'list' | 'chat' | 'contact'`) in `SmartsuppChatsPage` controls which pane is visible on narrow viewports via Tailwind `md:` breakpoints. The existing `listPanelOpen` / `contactPanelOpen` desktop states are preserved unchanged. On mobile, topic-hint chips collapse into a new `TopicPickerSheet` bottom-drawer. The desktop `MobileNotice` banner is suppressed for this page only.

**Tech Stack:** React 18, TypeScript, Tailwind CSS, Vitest + React Testing Library (jest-dom), lucide-react.

---

## File Map

**Created:**
- `frontend/src/components/customer-support/smartsupp/TopicPickerSheet.tsx`
- `frontend/src/components/customer-support/smartsupp/__tests__/TopicPickerSheet.test.tsx`
- `frontend/src/components/Layout/__tests__/Layout.test.tsx`

**Modified:**
- `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`
- `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`
- `frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx`
- `frontend/src/components/Layout/Layout.tsx`
- `frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx`
- `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx`
- `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyTriggerBar.test.tsx`

---

## Task 1: TopicPickerSheet — new bottom-sheet component

**Files:**
- Create: `frontend/src/components/customer-support/smartsupp/TopicPickerSheet.tsx`
- Create: `frontend/src/components/customer-support/smartsupp/__tests__/TopicPickerSheet.test.tsx`

- [ ] **Step 1.1: Write failing tests**

Create `frontend/src/components/customer-support/smartsupp/__tests__/TopicPickerSheet.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import TopicPickerSheet from "../TopicPickerSheet";

describe("TopicPickerSheet", () => {
  const onSelect = jest.fn();
  const onClose = jest.fn();

  beforeEach(() => {
    onSelect.mockClear();
    onClose.mockClear();
  });

  it("renders nothing when isOpen is false", () => {
    const { container } = render(
      <TopicPickerSheet isOpen={false} onSelect={onSelect} onClose={onClose} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it("renders all five DRAFT_REPLY_HINTS when isOpen is true", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    expect(screen.getByRole("button", { name: "Výměna zboží" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reklamace" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Doprava" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Platba" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Vrácení zboží" })).toBeInTheDocument();
  });

  it("calls onSelect with the hint label when a topic button is clicked", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: "Reklamace" }));
    expect(onSelect).toHaveBeenCalledWith("Reklamace");
    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it("calls onClose when the backdrop is clicked", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    fireEvent.click(screen.getByTestId("topic-picker-backdrop"));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("does not call onClose when the sheet panel itself is clicked", () => {
    render(<TopicPickerSheet isOpen={true} onSelect={onSelect} onClose={onClose} />);
    fireEvent.click(screen.getByRole("dialog"));
    expect(onClose).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 1.2: Run tests and confirm failure**

```bash
cd /path/to/project/frontend && npx jest TopicPickerSheet --no-coverage 2>&1 | tail -20
```

Expected: `Cannot find module '../TopicPickerSheet'`

- [ ] **Step 1.3: Implement TopicPickerSheet**

Create `frontend/src/components/customer-support/smartsupp/TopicPickerSheet.tsx`:

```tsx
import React from "react";
import { DRAFT_REPLY_HINTS } from "./draftReplyHints";

interface TopicPickerSheetProps {
  isOpen: boolean;
  onSelect: (label: string) => void;
  onClose: () => void;
}

const TopicPickerSheet: React.FC<TopicPickerSheetProps> = ({ isOpen, onSelect, onClose }) => {
  if (!isOpen) return null;

  return (
    <div
      data-testid="topic-picker-backdrop"
      className="fixed inset-0 z-50 flex items-end bg-black/40"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Vyberte téma"
        className="bg-white rounded-t-2xl w-full"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-4 py-3 border-b border-gray-200">
          <p className="font-semibold text-gray-900">Vyberte téma</p>
        </div>
        <ul>
          {DRAFT_REPLY_HINTS.map((hint) => (
            <li key={hint.id}>
              <button
                type="button"
                onClick={() => onSelect(hint.label)}
                className="w-full text-left px-4 py-3 text-sm text-gray-800 hover:bg-gray-50 min-h-[44px] flex items-center"
              >
                {hint.label}
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
};

export default TopicPickerSheet;
```

- [ ] **Step 1.4: Run tests and confirm they pass**

```bash
cd frontend && npx jest TopicPickerSheet --no-coverage 2>&1 | tail -10
```

Expected: `Tests: 5 passed`

- [ ] **Step 1.5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/TopicPickerSheet.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/TopicPickerSheet.test.tsx
git commit -m "feat: add TopicPickerSheet bottom-sheet for mobile topic selection"
```

---

## Task 2: DraftReplyTriggerBar — add mobile variant

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyTriggerBar.test.tsx`

Context: After this change, the component renders two separate rows — desktop (`hidden md:flex`) and mobile (`flex md:hidden`). Both rows include a "Generovat odpověď" button, so JSDOM (which does not apply CSS) will find two buttons with that name. The three existing tests that interact with the generate button must be updated to use `getAllByRole` or `getAllByTestId`.

- [ ] **Step 2.1: Write new failing tests**

Add these four cases inside the existing `describe("DraftReplyTriggerBar", ...)` block in `DraftReplyTriggerBar.test.tsx`, after the last existing `it(...)`:

```tsx
  it("renders a Témata button in the mobile row", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /témata/i })).toBeInTheDocument();
  });

  it("opens TopicPickerSheet when Témata is clicked", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /témata/i }));
    expect(screen.getByTestId("topic-picker-backdrop")).toBeInTheDocument();
  });

  it("calls onGenerate with label when a topic is picked from the sheet", () => {
    const onGenerate = jest.fn();
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={onGenerate}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /témata/i }));
    fireEvent.click(screen.getByRole("button", { name: "Doprava" }));
    expect(onGenerate).toHaveBeenCalledWith("Doprava");
  });

  it("disables the Témata button when disabled is true", () => {
    render(
      <DraftReplyTriggerBar
        disabled={true}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /témata/i })).toBeDisabled();
  });
```

- [ ] **Step 2.2: Run tests and confirm new ones fail**

```bash
cd frontend && npx jest DraftReplyTriggerBar --no-coverage 2>&1 | tail -15
```

Expected: 4 failures — `Unable to find an accessible element with the role "button" and name /témata/i`

- [ ] **Step 2.3: Implement the mobile variant**

Replace the full content of `frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx`:

```tsx
import { useState } from "react";
import { Sparkles, Tag } from "lucide-react";
import { DRAFT_REPLY_HINTS } from "./draftReplyHints";
import TopicPickerSheet from "./TopicPickerSheet";

interface DraftReplyTriggerBarProps {
  disabled: boolean;
  canGenerateWithoutTopic: boolean;
  error: string | null;
  onGenerate: (topic?: string) => void;
}

function DraftReplyTriggerBar({
  disabled,
  canGenerateWithoutTopic,
  error,
  onGenerate,
}: DraftReplyTriggerBarProps) {
  const [topicPickerOpen, setTopicPickerOpen] = useState(false);

  return (
    <div className="border-t border-gray-100 bg-gray-50 px-4 py-2">
      {/* Desktop: chip row + generate button */}
      <div className="hidden md:flex flex-wrap items-center gap-2">
        {DRAFT_REPLY_HINTS.map((hint) => (
          <button
            key={hint.id}
            type="button"
            disabled={disabled}
            onClick={() => onGenerate(hint.label)}
            className="inline-flex items-center rounded-full px-3 py-1 text-xs bg-white border border-gray-200 text-gray-700 hover:bg-blue-50 hover:border-blue-300 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {hint.label}
          </button>
        ))}
        <button
          type="button"
          data-testid="generate-reply-desktop"
          disabled={disabled || !canGenerateWithoutTopic}
          onClick={() => onGenerate(undefined)}
          title={canGenerateWithoutTopic ? undefined : "Konverzace neobsahuje zprávu zákazníka"}
          className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium bg-blue-500 text-white hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Sparkles className="w-3.5 h-3.5" />
          Generovat odpověď
        </button>
      </div>

      {/* Mobile: Témata + generate buttons */}
      <div className="flex md:hidden items-center gap-2">
        <button
          type="button"
          disabled={disabled}
          onClick={() => setTopicPickerOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-full px-4 py-2.5 text-sm border border-gray-200 bg-white text-gray-700 hover:bg-gray-50 transition-colors disabled:opacity-50 disabled:cursor-not-allowed min-h-[40px]"
        >
          <Tag className="w-4 h-4" />
          Témata
        </button>
        <button
          type="button"
          data-testid="generate-reply-mobile"
          disabled={disabled || !canGenerateWithoutTopic}
          onClick={() => onGenerate(undefined)}
          title={canGenerateWithoutTopic ? undefined : "Konverzace neobsahuje zprávu zákazníka"}
          className="inline-flex items-center gap-1.5 rounded-full px-4 py-2.5 text-sm font-medium bg-blue-500 text-white hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed min-h-[40px]"
        >
          <Sparkles className="w-4 h-4" />
          Generovat odpověď
        </button>
      </div>

      {error && <p className="mt-1.5 text-xs text-red-600">{error}</p>}

      <TopicPickerSheet
        isOpen={topicPickerOpen}
        onSelect={(label) => {
          setTopicPickerOpen(false);
          onGenerate(label);
        }}
        onClose={() => setTopicPickerOpen(false)}
      />
    </div>
  );
}

export default DraftReplyTriggerBar;
```

- [ ] **Step 2.4: Update three existing tests that now find two "Generovat odpověď" buttons**

In `DraftReplyTriggerBar.test.tsx`, update these three `it(...)` bodies (keep the description strings unchanged):

**"calls onGenerate with undefined when the generate button is clicked"** — change `getByRole` to `getByTestId`:
```tsx
  it("calls onGenerate with undefined when the generate button is clicked", () => {
    const onGenerate = jest.fn();
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={onGenerate}
      />,
    );
    fireEvent.click(screen.getByTestId("generate-reply-desktop"));
    expect(onGenerate).toHaveBeenCalledWith(undefined);
  });
```

**"disables the generate button when canGenerateWithoutTopic is false"** — check both buttons:
```tsx
  it("disables the generate button when canGenerateWithoutTopic is false", () => {
    render(
      <DraftReplyTriggerBar
        disabled={false}
        canGenerateWithoutTopic={false}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByTestId("generate-reply-desktop")).toBeDisabled();
    expect(screen.getByTestId("generate-reply-mobile")).toBeDisabled();
  });
```

**"disables every control while disabled is true"** — add Témata and mobile generate:
```tsx
  it("disables every control while disabled is true", () => {
    render(
      <DraftReplyTriggerBar
        disabled={true}
        canGenerateWithoutTopic={true}
        error={null}
        onGenerate={jest.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: "Reklamace" })).toBeDisabled();
    expect(screen.getByTestId("generate-reply-desktop")).toBeDisabled();
    expect(screen.getByTestId("generate-reply-mobile")).toBeDisabled();
    expect(screen.getByRole("button", { name: /témata/i })).toBeDisabled();
  });
```

- [ ] **Step 2.5: Run all DraftReplyTriggerBar tests and confirm they pass**

```bash
cd frontend && npx jest DraftReplyTriggerBar --no-coverage 2>&1 | tail -10
```

Expected: `Tests: 10 passed` (6 original + 4 new)

- [ ] **Step 2.6: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/DraftReplyTriggerBar.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/DraftReplyTriggerBar.test.tsx
git commit -m "feat: add mobile variant to DraftReplyTriggerBar with TopicPickerSheet"
```

---

## Task 3: ConversationDetail — add optional mobile nav props

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx`

The two new props (`onBack`, `onOpenContactDetails`) are optional so all existing callsites continue to work. When provided, mobile-only buttons (`md:hidden`) render in the header. `data-testid` attributes make the buttons findable in tests regardless of viewport.

- [ ] **Step 3.1: Write new failing tests**

Add these six cases inside the existing `describe("ConversationDetail", ...)` block, after the last existing `it(...)`:

```tsx
  it("renders a back button when onBack is provided", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} onBack={jest.fn()} />));
    expect(screen.getByTestId("back-to-list-btn")).toBeInTheDocument();
  });

  it("does not render a back button when onBack is omitted", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.queryByTestId("back-to-list-btn")).not.toBeInTheDocument();
  });

  it("calls onBack when the back button is clicked", () => {
    const onBack = jest.fn();
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} onBack={onBack} />));
    fireEvent.click(screen.getByTestId("back-to-list-btn"));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it("renders an info button when onOpenContactDetails is provided", () => {
    render(
      wrap(
        <ConversationDetail
          conversationId="c1"
          conversation={conv}
          onOpenContactDetails={jest.fn()}
        />,
      ),
    );
    expect(screen.getByTestId("open-contact-details-btn")).toBeInTheDocument();
  });

  it("does not render an info button when onOpenContactDetails is omitted", () => {
    render(wrap(<ConversationDetail conversationId="c1" conversation={conv} />));
    expect(screen.queryByTestId("open-contact-details-btn")).not.toBeInTheDocument();
  });

  it("calls onOpenContactDetails when the info button is clicked", () => {
    const onOpenContactDetails = jest.fn();
    render(
      wrap(
        <ConversationDetail
          conversationId="c1"
          conversation={conv}
          onOpenContactDetails={onOpenContactDetails}
        />,
      ),
    );
    fireEvent.click(screen.getByTestId("open-contact-details-btn"));
    expect(onOpenContactDetails).toHaveBeenCalledTimes(1);
  });
```

- [ ] **Step 3.2: Run tests and confirm new ones fail**

```bash
cd frontend && npx jest ConversationDetail --no-coverage 2>&1 | tail -15
```

Expected: 6 failures — `Unable to find an element by: [data-testid="back-to-list-btn"]`

- [ ] **Step 3.3: Implement the mobile nav buttons**

Replace the full content of `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`:

```tsx
import React, { useEffect, useRef } from "react";
import { ArrowLeft, Info } from "lucide-react";
import { ConversationDto, MessageDto, useSmartsuppConversation } from "../../../api/hooks/useSmartsupp";
import MessageBubble from "./MessageBubble";
import StatusPill from "./StatusPill";
import AgentBadge from "./AgentBadge";
import DaySeparator from "./DaySeparator";
import ChatComposer from "./ChatComposer";

interface ConversationDetailProps {
  conversationId: string;
  conversation: ConversationDto;
  onBack?: () => void;
  onOpenContactDetails?: () => void;
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
  onBack,
  onOpenContactDetails,
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
      <div className="px-4 py-3 border-b border-gray-200 flex items-center gap-2">
        {onBack && (
          <button
            type="button"
            data-testid="back-to-list-btn"
            onClick={onBack}
            aria-label="Zpět"
            className="md:hidden flex items-center justify-center min-h-[40px] min-w-[40px] p-1 -ml-1 text-gray-600 flex-shrink-0"
          >
            <ArrowLeft className="w-5 h-5" />
          </button>
        )}
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold text-gray-900 truncate">{displayName}</h3>
            <StatusPill status={conversation.status} />
          </div>
          {conversation.contactEmail && (
            <p className="text-xs text-gray-500 truncate">{conversation.contactEmail}</p>
          )}
        </div>
        <div className="ml-auto flex items-center gap-2 flex-shrink-0">
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={id} />
          ))}
          {onOpenContactDetails && (
            <button
              type="button"
              data-testid="open-contact-details-btn"
              onClick={onOpenContactDetails}
              aria-label="Detail kontaktu"
              className="md:hidden flex items-center justify-center min-h-[40px] min-w-[40px] p-1 text-gray-600"
            >
              <Info className="w-5 h-5" />
            </button>
          )}
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
        conversationId={conversationId}
        lastContactMessage={lastContactMessage(messages)}
      />
    </div>
  );
};

export default ConversationDetail;
```

- [ ] **Step 3.4: Run all ConversationDetail tests and confirm they pass**

```bash
cd frontend && npx jest ConversationDetail --no-coverage 2>&1 | tail -10
```

Expected: `Tests: 11 passed` (5 original + 6 new)

- [ ] **Step 3.5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx \
        frontend/src/components/customer-support/smartsupp/__tests__/ConversationDetail.test.tsx
git commit -m "feat: add optional onBack and onOpenContactDetails props to ConversationDetail"
```

---

## Task 4: SmartsuppChatsPage — responsive multi-pane layout

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx`

**Design decisions:**
- `listPanelOpen` continues to gate the list pane via conditional rendering so existing DOM-based tests keep passing. CSS class `${mobileView === 'list' ? 'flex' : 'hidden md:flex'}` handles mobile visibility on top of that.
- Both `CollapsibleRail`s are wrapped in `<div className="hidden md:flex">` so they don't consume space on mobile, but are still present in the DOM in JSDOM so existing rail tests still work.
- The desktop contact block changes `hidden lg:flex` → `hidden md:flex` (per spec) — still hidden in JSDOM since it's CSS.
- Mobile contact subpage is conditionally rendered (not CSS-only) so tests can detect its presence in the DOM.

- [ ] **Step 4.1: Write new failing tests**

Add these four cases after the last existing `it(...)` in `SmartsuppChatsPage.test.tsx`:

```tsx
  it("shows navigation buttons in ConversationDetail after a conversation is selected", () => {
    render(wrap(<SmartsuppChatsPage />));
    expect(screen.queryByTestId("back-to-list-btn")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    expect(screen.getByTestId("back-to-list-btn")).toBeInTheDocument();
    expect(screen.getByTestId("open-contact-details-btn")).toBeInTheDocument();
  });

  it("clicking the info button shows the mobile contact subpage", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    expect(screen.queryByTestId("mobile-contact-subpage")).not.toBeInTheDocument();
    fireEvent.click(screen.getByTestId("open-contact-details-btn"));
    expect(screen.getByTestId("mobile-contact-subpage")).toBeInTheDocument();
  });

  it("the contact back button hides the mobile contact subpage", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    fireEvent.click(screen.getByTestId("open-contact-details-btn"));
    expect(screen.getByTestId("mobile-contact-subpage")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("back-to-chat-btn"));
    expect(screen.queryByTestId("mobile-contact-subpage")).not.toBeInTheDocument();
  });

  it("the chat back button does not show the mobile contact subpage", () => {
    render(wrap(<SmartsuppChatsPage />));
    fireEvent.click(screen.getByRole("button", { name: /Jana Nováková/ }));
    fireEvent.click(screen.getByTestId("back-to-list-btn"));
    expect(screen.queryByTestId("mobile-contact-subpage")).not.toBeInTheDocument();
  });
```

- [ ] **Step 4.2: Run tests and confirm new ones fail**

```bash
cd frontend && npx jest SmartsuppChatsPage --no-coverage 2>&1 | tail -15
```

Expected: 4 failures — `Unable to find an element by: [data-testid="back-to-list-btn"]`

- [ ] **Step 4.3: Implement the responsive layout**

Replace the full content of `frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx`:

```tsx
import React, { useState } from "react";
import { ArrowLeft } from "lucide-react";
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

type MobileView = "list" | "chat" | "contact";

const SmartsuppChatsPage: React.FC = () => {
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState<"Open" | "Resolved">("Open");
  const [mobileView, setMobileView] = useState<MobileView>("list");
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
      {/* Sync bar — hidden on mobile when not on the list pane */}
      <div
        className={`${mobileView !== "list" ? "hidden md:flex" : "flex"} items-center justify-end px-4 py-2 border-b border-gray-200 bg-white`}
      >
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
        {/* List pane — conditional rendering preserves existing desktop rail tests;
            CSS class controls mobile visibility on top of that */}
        {listPanelOpen && (
          <div
            className={`${mobileView === "list" ? "flex" : "hidden md:flex"} flex-col w-full md:w-96 flex-shrink-0 overflow-hidden`}
          >
            <ConversationList
              conversations={conversations}
              selectedId={selectedId}
              status={status}
              isLoading={isLoading}
              onSelect={(id) => {
                setSelectedId(id);
                setMobileView("chat");
              }}
              onStatusChange={(s) => {
                setStatus(s);
                setSelectedId(null);
                setMobileView("list");
              }}
            />
          </div>
        )}

        {/* Left rail — desktop only */}
        <div className="hidden md:flex">
          <CollapsibleRail
            side="left"
            isOpen={listPanelOpen}
            label="Seznam konverzací"
            onToggle={() => togglePanel(LIST_PANEL_KEY, setListPanelOpen)}
          />
        </div>

        {/* Chat pane — always rendered; CSS hides it on mobile when another pane is active */}
        <div
          className={`${mobileView === "chat" ? "flex" : "hidden"} md:flex flex-col flex-1 overflow-hidden min-w-0`}
        >
          {selectedConversation ? (
            <ConversationDetail
              conversationId={selectedId!}
              conversation={selectedConversation}
              onBack={() => setMobileView("list")}
              onOpenContactDetails={() => setMobileView("contact")}
            />
          ) : (
            <div className="flex items-center justify-center h-full text-gray-400 text-sm">
              Vyberte konverzaci
            </div>
          )}
        </div>

        {/* Desktop contact panel — hidden on mobile */}
        {selectedConversation && (
          <div className="hidden md:flex">
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

        {/* Mobile contact subpage — conditionally rendered so tests can detect it in DOM */}
        {mobileView === "contact" && selectedConversation && (
          <div
            data-testid="mobile-contact-subpage"
            className="flex flex-col w-full md:hidden overflow-hidden"
          >
            <div className="flex items-center gap-2 px-4 py-3 border-b border-gray-200 bg-white">
              <button
                type="button"
                data-testid="back-to-chat-btn"
                onClick={() => setMobileView("chat")}
                aria-label="Zpět na konverzaci"
                className="flex items-center justify-center min-h-[40px] min-w-[40px] p-1 -ml-1 text-gray-600 flex-shrink-0"
              >
                <ArrowLeft className="w-5 h-5" />
              </button>
              <span className="font-semibold text-gray-900">Detail kontaktu</span>
            </div>
            <div className="flex-1 overflow-y-auto">
              <ContactDetailsPanel conversation={selectedConversation} />
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default SmartsuppChatsPage;
```

- [ ] **Step 4.4: Run all SmartsuppChatsPage tests and confirm they all pass**

```bash
cd frontend && npx jest SmartsuppChatsPage --no-coverage 2>&1 | tail -15
```

Expected: `Tests: 11 passed` (7 original + 4 new). If any original test fails, see the note below.

> **Note — if "collapses the conversation list" or "restores a collapsed list" fails:** These tests check `queryByText("Všechny konverzace")` after setting `listPanelOpen=false`. Because the `{listPanelOpen && ...}` conditional rendering guard is preserved, those tests should continue to work. If they fail, confirm that the list pane's outer wrapper is still the same `{listPanelOpen && (...)}` conditional, not a pure CSS approach.

- [ ] **Step 4.5: Commit**

```bash
git add frontend/src/components/customer-support/smartsupp/pages/SmartsuppChatsPage.tsx \
        frontend/src/components/customer-support/smartsupp/pages/__tests__/SmartsuppChatsPage.test.tsx
git commit -m "feat: make SmartsuppChatsPage responsive with single-pane mobile navigation"
```

---

## Task 5: Layout — suppress MobileNotice on the Smartsupp page

**Files:**
- Modify: `frontend/src/components/Layout/Layout.tsx`
- Create: `frontend/src/components/Layout/__tests__/Layout.test.tsx`

- [ ] **Step 5.1: Write a failing test**

Create `frontend/src/components/Layout/__tests__/Layout.test.tsx`:

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import Layout from "../Layout";

jest.mock("../Sidebar", () => ({ default: () => <div data-testid="sidebar" /> }));
jest.mock("../TopBar", () => ({ default: (_props: unknown) => <div data-testid="topbar" /> }));
jest.mock("../../common/MobileNotice", () => ({
  MobileNotice: () => <div data-testid="mobile-notice" />,
}));

const wrap = (path: string) => (
  <MemoryRouter initialEntries={[path]}>
    <Layout>
      <div>Content</div>
    </Layout>
  </MemoryRouter>
);

describe("Layout", () => {
  it("does not show MobileNotice on the dashboard page", () => {
    render(wrap("/"));
    expect(screen.queryByTestId("mobile-notice")).not.toBeInTheDocument();
  });

  it("shows MobileNotice on other pages", () => {
    render(wrap("/manufacture"));
    expect(screen.getByTestId("mobile-notice")).toBeInTheDocument();
  });

  it("does not show MobileNotice on the smartsupp page", () => {
    render(wrap("/customer/smartsupp"));
    expect(screen.queryByTestId("mobile-notice")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 5.2: Run tests and confirm the third case fails**

```bash
cd frontend && npx jest Layout --no-coverage 2>&1 | tail -15
```

Expected: the third test (`smartsupp page`) fails — `MobileNotice` is found when it shouldn't be.

- [ ] **Step 5.3: Implement the fix in Layout.tsx**

In `frontend/src/components/Layout/Layout.tsx`, replace the `isDashboardPage` constant and the render expression that uses it:

```tsx
  // Don't show mobile notice on dashboard or on the now-mobile-ready smartsupp page
  const hideMobileNotice =
    location.pathname === "/" ||
    location.pathname === "/dashboard" ||
    location.pathname === "/customer/smartsupp";
```

And in the JSX, change:
```tsx
        {!isDashboardPage && <MobileNotice />}
```
to:
```tsx
        {!hideMobileNotice && <MobileNotice />}
```

- [ ] **Step 5.4: Run all Layout tests and confirm they pass**

```bash
cd frontend && npx jest Layout --no-coverage 2>&1 | tail -10
```

Expected: `Tests: 3 passed`

- [ ] **Step 5.5: Commit**

```bash
git add frontend/src/components/Layout/Layout.tsx \
        frontend/src/components/Layout/__tests__/Layout.test.tsx
git commit -m "feat: suppress MobileNotice on the now-mobile-ready smartsupp page"
```

---

## Task 6: Verification

- [ ] **Step 6.1: Full build + lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: zero errors.

- [ ] **Step 6.2: Run all Smartsupp unit tests**

```bash
cd frontend && npx jest --testPathPattern="smartsupp|Layout" --no-coverage 2>&1 | tail -20
```

Expected: all pass.

- [ ] **Step 6.3: Check coverage on changed files**

```bash
cd frontend && npx jest --testPathPattern="smartsupp|Layout" --coverage --collectCoverageFrom="src/components/customer-support/smartsupp/**/*.tsx,src/components/Layout/Layout.tsx" 2>&1 | grep -E "^(All files|.*smartsupp.*|.*Layout.*)"
```

Expected: ≥ 80% on each changed file.

- [ ] **Step 6.4: Manual smoke test**

1. Start dev server: `cd frontend && npm run dev`
2. Open DevTools → Device Toolbar → 375 px width, navigate to `/customer/smartsupp`.
3. Verify:
   - Conversation list fills the screen; tapping a row opens a full-screen chat.
   - Back arrow (←) returns to the list.
   - Info button (ⓘ) opens the contact subpage with its own back button.
   - "Témata" opens the bottom-sheet picker; picking a topic triggers draft generation; regenerate/discard work; textarea and Send are reachable.
   - No "optimised for desktop" banner.
4. Resize to ≥ 768 px — the original three-pane desktop layout is unchanged.
