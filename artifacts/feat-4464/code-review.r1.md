## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/components/marketing/calendar/MobileAgendaView.tsx:72` — still passes raw `a.folderLinks` (`folderKey`) through; display-only and out of scope per spec, but inconsistent with the list/detail mapping now done in `MarketingCalendarPage.tsx`.
