### task: fix-smartsupp-consumer-optionality-fallout

Every generated Smartsupp DTO field is optional (`field?: T`), unlike the hand-declared interfaces deleted in the previous task which mixed required and optional fields. This task widens the handful of consuming components that assumed required fields. Every fix below was verified to compile against this repo's actual `tsconfig.json` (`strict: true`) using `npx -p typescript@4.9.5 tsc --noEmit -p tsconfig.json` before being written into this plan — these are not speculative.

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/StatusPill.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/DaySeparator.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationList.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx`
- Modify: `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`

#### Step 1: Widen `StatusPill.tsx` to accept an optional status

`conversation.status` is now `string | undefined` everywhere it's read. Rather than adding a `?? ""` fallback at each of `StatusPill`'s three call sites, widen the one component that owns the fallback logic.

In `frontend/src/components/customer-support/smartsupp/StatusPill.tsx`, replace:

```tsx
interface StatusPillProps {
  status: string;
}

interface PillStyle {
  label: string;
  className: string;
}

function resolvePill(status: string): PillStyle {
  switch (status.toLowerCase()) {
```

with:

```tsx
interface StatusPillProps {
  status?: string;
}

interface PillStyle {
  label: string;
  className: string;
}

function resolvePill(status?: string): PillStyle {
  switch ((status ?? "").toLowerCase()) {
```

And replace the `default` branch's `label: status,` with `label: status ?? "",` (it's the only other read of the now-optional parameter in that function).

#### Step 2: Widen `DaySeparator.tsx` to accept `Date | string`

`MessageDto.createdAt` is now `Date | undefined` (was `string`, required). In `frontend/src/components/customer-support/smartsupp/DaySeparator.tsx`, replace:

```tsx
interface DaySeparatorProps {
  date: string;
}

function formatDayLabel(dateStr: string): string {
```

with:

```tsx
interface DaySeparatorProps {
  date: Date | string;
}

function formatDayLabel(dateStr: Date | string): string {
```

No other change needed in this file — `new Date(dateStr)` already accepts both `Date` and `string`.

#### Step 3: Fix `MessageBubble.tsx`

`MessageDto.authorType` is now `string | undefined` and `MessageDto.createdAt` is `Date | undefined`. In `frontend/src/components/customer-support/smartsupp/MessageBubble.tsx`:

Replace:
```tsx
function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });
}
```
with:
```tsx
function formatTime(dateStr: Date | string): string {
  return new Date(dateStr).toLocaleTimeString("cs-CZ", { hour: "2-digit", minute: "2-digit" });
}
```

Replace:
```tsx
  const authorType = message.authorType.toLowerCase();
```
with:
```tsx
  const authorType = (message.authorType ?? "").toLowerCase();
```

`formatTime(message.createdAt)` at the call site needs no change — `message.createdAt` being `Date | undefined` and the function parameter being optional-compatible would still fail for `undefined`, so instead fix the call site itself. Replace:
```tsx
          <span>{formatTime(message.createdAt)}</span>
```
with:
```tsx
          <span>{formatTime(message.createdAt ?? new Date(0))}</span>
```

(`createdAt` is a non-nullable persisted field on every real message; the optionality here is purely a TS-strictness artifact of NSwag generating every field as optional, not a real runtime possibility — the epoch fallback is defensive only.)

#### Step 4: Fix `ConversationListItem.tsx`

`ConversationDto.updatedAt` is now `Date | undefined` (was `string`, required); `.lastMessageAt` was already `Date | undefined`-equivalent before. In `frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx`, replace:

```tsx
function formatRelativeTime(dateStr?: string | null): string {
  if (!dateStr) return "";
  const diff = Date.now() - new Date(dateStr).getTime();
```
with:
```tsx
function formatRelativeTime(dateStr?: Date | string | null): string {
  if (!dateStr) return "";
  const diff = Date.now() - new Date(dateStr).getTime();
```

No other change needed in this file — `formatRelativeTime(conversation.lastMessageAt ?? conversation.updatedAt)` already passes an optional value into what is now an optional-accepting parameter, and `StatusPill status={conversation.status}` is fine once Step 1 lands.

#### Step 5: Fix `ConversationList.tsx`

`ConversationDto.lastMessageAt`/`.updatedAt` are both `Date | undefined`; comparing two possibly-undefined values with `<`/`>` is a compile error. In `frontend/src/components/customer-support/smartsupp/ConversationList.tsx`, replace:

```tsx
      {[...conversations]
        .sort((a, b) => {
          const aTime = a.lastMessageAt ?? a.updatedAt;
          const bTime = b.lastMessageAt ?? b.updatedAt;
          return bTime < aTime ? -1 : bTime > aTime ? 1 : 0;
        })
        .map((c) => (
          <ConversationListItem
            key={c.id}
            conversation={c}
            isSelected={c.id === selectedId}
            onClick={() => onSelect(c.id)}
          />
        ))}
```

with:

```tsx
      {[...conversations]
        .sort((a, b) => {
          const aTime = a.lastMessageAt ?? a.updatedAt ?? new Date(0);
          const bTime = b.lastMessageAt ?? b.updatedAt ?? new Date(0);
          return bTime < aTime ? -1 : bTime > aTime ? 1 : 0;
        })
        .map((c) => (
          <ConversationListItem
            key={c.id}
            conversation={c}
            isSelected={c.id === selectedId}
            onClick={() => onSelect(c.id ?? "")}
          />
        ))}
```

(`onSelect` requires a `string`; `c.id` is now `string | undefined`. `c.id` is a real conversation's primary key and will always be present in practice — the `?? ""` is a compile-time formality, matching the same pattern used elsewhere in this task.)

#### Step 6: Fix `ConversationDetail.tsx`

`MessageDto.authorType`/`.createdAt`, `ConversationDto.status`/`.assignedAgentIds` are all now optional. In `frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx`:

Replace:
```tsx
export function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const authorType = m.authorType.toLowerCase();
```
with:
```tsx
export function lastContactMessage(messages: MessageDto[]): string | null {
  for (let i = messages.length - 1; i >= 0; i--) {
    const m = messages[i];
    const authorType = (m.authorType ?? "").toLowerCase();
```

Replace:
```tsx
function groupByDay(messages: MessageDto[]): Array<{ day: string; items: MessageDto[] }> {
  const groups: Array<{ day: string; items: MessageDto[] }> = [];
  for (const m of messages) {
    const day = new Date(m.createdAt).toISOString().slice(0, 10);
```
with:
```tsx
function groupByDay(messages: MessageDto[]): Array<{ day: string; items: MessageDto[] }> {
  const groups: Array<{ day: string; items: MessageDto[] }> = [];
  for (const m of messages) {
    const day = new Date(m.createdAt ?? new Date(0)).toISOString().slice(0, 10);
```

Replace:
```tsx
          {conversation.assignedAgentIds.map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {liveStatus.toLowerCase() === 'open' && (
```
with:
```tsx
          {(conversation.assignedAgentIds ?? []).map((id) => (
            <AgentBadge key={id} agentId={id} name={agentNames[id] ?? id} />
          ))}
          {(liveStatus ?? "").toLowerCase() === 'open' && (
```

Replace:
```tsx
            <DaySeparator date={g.items[0].createdAt} />
```
with:
```tsx
            <DaySeparator date={g.items[0].createdAt ?? new Date(0)} />
```

`StatusPill status={liveStatus}` needs no change (fine once Task-1-Step-1 widens `StatusPill`, and `liveStatus`'s type is now `string | undefined`, which the widened prop accepts).

#### Step 7: Fix `ContactDetailsPanel.tsx`

`ConversationDto.variables`/`.contactProperties`/`.assignedAgentIds`/`.contactTags`/`.tags`/`.otherConversations` are all now optional. In `frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx`:

Replace:
```tsx
  const infoEntries = mergedInfoEntries(conversation.variables, conversation.contactProperties);
```
with:
```tsx
  const infoEntries = mergedInfoEntries(conversation.variables ?? {}, conversation.contactProperties ?? {});
```

Replace:
```tsx
      {conversation.assignedAgentIds.length > 0 && (
        <Section title="Přiřazení operátoři">
          <div className="flex flex-wrap gap-1.5">
            {conversation.assignedAgentIds.map((id) => (
```
with:
```tsx
      {(conversation.assignedAgentIds ?? []).length > 0 && (
        <Section title="Přiřazení operátoři">
          <div className="flex flex-wrap gap-1.5">
            {(conversation.assignedAgentIds ?? []).map((id) => (
```

Replace:
```tsx
      {conversation.contactTags.length > 0 && (
        <Section title="Štítky kontaktu">
          <div className="flex flex-wrap gap-1.5">
            {conversation.contactTags.map((t) => (
```
with:
```tsx
      {(conversation.contactTags ?? []).length > 0 && (
        <Section title="Štítky kontaktu">
          <div className="flex flex-wrap gap-1.5">
            {(conversation.contactTags ?? []).map((t) => (
```

Replace:
```tsx
      {conversation.tags.length > 0 && (
        <Section title="Štítky">
          <div className="flex flex-wrap gap-1.5">
            {conversation.tags.map((t) => (
```
with:
```tsx
      {(conversation.tags ?? []).length > 0 && (
        <Section title="Štítky">
          <div className="flex flex-wrap gap-1.5">
            {(conversation.tags ?? []).map((t) => (
```

Replace:
```tsx
      {conversation.otherConversations.length > 0 && (
        <Section title={`Jiné konverzace (${conversation.otherConversations.length})`}>
          {conversation.otherConversations.map((c) => (
```
with:
```tsx
      {(conversation.otherConversations ?? []).length > 0 && (
        <Section title={`Jiné konverzace (${(conversation.otherConversations ?? []).length})`}>
          {(conversation.otherConversations ?? []).map((c) => (
```

`StatusPill status={conversation.status}` needs no change (fine once `StatusPill` is widened). `conv.id`/`conv.status`/`conv.lastMessageAt` inside `OtherConversationRow` need no change: `conv.status`/`conv.lastMessagePreview` are read only as JSX children (accept `undefined`), `conv.lastMessageAt` is already guarded by `conv.lastMessageAt ? new Date(conv.lastMessageAt)... : "—"`, and `onSelect?.(conv.id)` — `onSelect` is itself optional (`(id: string) => void | undefined`) but `conv.id` being `string | undefined` passed as the required `id: string` argument **does** need a fix. Replace:
```tsx
      onClick={() => onSelect?.(conv.id)}
```
with:
```tsx
      onClick={() => onSelect?.(conv.id ?? "")}
```

#### Step 8: Fix `ShoptetCustomerCard.tsx`

`ShoptetContactInfoDto.recentOrders` is now `ShoptetOrderSnapshotDto[] | undefined` (was required). In `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`, replace:

```tsx
  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  const hasCustomer = customer != null;
  const hasOrders = recentOrders.length > 0;
```
with:
```tsx
  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  const hasCustomer = customer != null;
  const hasOrders = (recentOrders ?? []).length > 0;
```

Replace:
```tsx
            {recentOrders.map((order) => (
```
with:
```tsx
            {(recentOrders ?? []).map((order) => (
```

No other change needed in this file — `customer != null`/`cartUpdatedAt != null` guards already narrow correctly for both `null` and `undefined`, and everything inside those guards was already reading optional fields defensively.

#### Step 9: Verify the full build

```bash
cd frontend
npm run build
```

Expect zero TypeScript errors. If any remain, they will be additional Smartsupp-DTO-optionality fallout in a file not listed above — fix with the same `?? fallback` pattern used throughout this task; do not introduce `as any`/`as unknown as X` casts (forbidden by NFR-1).

```bash
npm run lint
```

Expect no new warnings.

#### Step 10: Run the Smartsupp component test suites

```bash
cd frontend
CI=true npx react-scripts test src/components/customer-support/smartsupp --watchAll=false
```

Expect all existing suites for these components to keep passing unchanged (this task is a pure type-fix pass with no behavior change).

#### Step 11: Commit

```bash
git add frontend/src/components/customer-support/smartsupp/StatusPill.tsx \
  frontend/src/components/customer-support/smartsupp/DaySeparator.tsx \
  frontend/src/components/customer-support/smartsupp/MessageBubble.tsx \
  frontend/src/components/customer-support/smartsupp/ConversationListItem.tsx \
  frontend/src/components/customer-support/smartsupp/ConversationList.tsx \
  frontend/src/components/customer-support/smartsupp/ConversationDetail.tsx \
  frontend/src/components/customer-support/smartsupp/ContactDetailsPanel.tsx \
  frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx
git commit -m "Fix Smartsupp components for generated-DTO field optionality"
```

`npm run build` should now be clean end-to-end for everything touched so far.

---
