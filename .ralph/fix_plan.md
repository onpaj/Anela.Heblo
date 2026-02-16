# Ralph Fix Plan - Mobile Dashboard Support

**PRD Reference:** `tasks/prd-mobile-dashboard-support.md`

**Goal:** Enable smartphone users (portrait, 320px-428px) to view Heblo Dashboard for quick monitoring with read-only, touch-optimized experience.

---

## Phase 1: Core Mobile Layout (HIGH PRIORITY)

### US-001: Mobile-responsive dashboard grid layout
- [x] Update DashboardGrid.tsx grid classes to `grid-cols-1 md:grid-cols-3 lg:grid-cols-6`
- [x] Remove tile size-specific column spans on mobile (all full-width)
- [x] Test grid layout on mobile viewports (320px, 375px, 428px)
- [x] Verify no horizontal scrolling occurs
- [x] Verify vertical spacing is touch-friendly (16px gap)
- [x] Run build: `npm run build`
- [x] Verify in browser with mobile device emulation

**Files:** `/frontend/src/components/dashboard/DashboardGrid.tsx`
**Commit:** `36c11bee feat: mobile-responsive dashboard grid layout (US-001)`

### US-002: Disable drag-and-drop on mobile
- [x] Create `useMediaQuery` hook for mobile detection (already exists)
- [x] Add mobile breakpoint detection to DashboardGrid.tsx
- [x] Conditionally render DndContext only on desktop (≥768px)
- [x] Pass isDragDisabled prop to tiles based on mobile detection
- [x] Test that scrolling is smooth without drag interference
- [x] Run build: `npm run build`
- [x] Verify in browser with mobile device emulation

**Files:** `/frontend/src/hooks/useMediaQuery.ts`, `/frontend/src/components/dashboard/DashboardGrid.tsx`, `/frontend/src/components/dashboard/DashboardTile.tsx`
**Commit:** `9e512ea0 feat: disable drag-and-drop on mobile (US-002)`

### US-006: Optimize dashboard header for mobile
- [x] Update Dashboard.tsx header title: `text-3xl` → `text-2xl md:text-3xl`
- [x] Hide or abbreviate description on mobile: `hidden sm:block`
- [x] Reduce header padding on mobile: `px-3 sm:px-4 md:px-6 lg:px-8`
- [x] Hide settings button on mobile: `hidden md:flex`
- [x] Verify header height ≤64px on mobile
- [x] Run build: `npm run build`
- [x] Verify in browser with mobile device emulation

**Files:** `/frontend/src/components/pages/Dashboard.tsx`

### US-007: Mobile viewport meta tag configuration
- [x] Update viewport meta tag in index.html
- [x] Add `width=device-width, initial-scale=1.0`
- [x] Add `maximum-scale=1.0` to prevent zoom on form inputs
- [x] Add `user-scalable=no` for consistent mobile experience
- [x] Test that app renders at 100% scale on mobile devices
- [x] Run build: `npm run build`

**Files:** `/frontend/public/index.html`

---

## Phase 2: Mobile Interactions (MEDIUM PRIORITY)

### US-003: Optimize tile content for mobile display
- [ ] Audit all tile components in `/frontend/src/components/dashboard/tiles/`
- [ ] Update tile titles: ensure min 16px font size on mobile
- [ ] Update CountTile: numbers min 24px, icons min 32px
- [ ] Update chart tiles (Production, InventorySummary): height 200px-300px
- [ ] Ensure line-height ≥1.5 for readability
- [ ] Test all 15+ tile types on mobile viewports
- [ ] Run typecheck: `npm run typecheck`
- [ ] Verify in browser with mobile device emulation

**Files:** All files in `/frontend/src/components/dashboard/tiles/`

### US-004: Mobile-friendly tile navigation
- [ ] Update clickable tiles to have min 44px tap target height
- [ ] Add active state styling for touch feedback (replace hover states)
- [ ] Remove 300ms touch delay (use `touch-action: manipulation`)
- [ ] Test navigation on tiles with `targetUrl` prop
- [ ] Run typecheck: `npm run typecheck`
- [ ] Verify in browser with mobile device emulation

**Files:** `/frontend/src/components/dashboard/DashboardTile.tsx`, `/frontend/src/components/dashboard/tiles/CountTile.tsx`

### US-005: Hide dashboard settings on mobile
- [x] Add mobile detection to Dashboard.tsx using `useIsMobile()` hook
- [x] Hide settings button on mobile: `hidden md:flex`
- [x] Ensure settings panel cannot be accessed on mobile: `showSettings && !isMobile`
- [x] Mobile users see all enabled tiles in configured order
- [x] Run build: `npm run build`
- [x] Verify in browser with mobile device emulation

**Files:** `/frontend/src/components/pages/Dashboard.tsx`

---

## Phase 3: Navigation & Notices (MEDIUM PRIORITY)

### US-008: Non-dashboard page mobile notice
- [ ] Create new MobileNotice component in `/frontend/src/components/common/`
- [ ] Add dismissible notice with sessionStorage persistence
- [ ] Style: `bg-yellow-50 border-l-4 border-yellow-400`
- [ ] Add AlertCircle icon from Lucide React
- [ ] Text: "This page is optimized for desktop. Some features may not work properly on mobile."
- [ ] Integrate into Layout component (exclude Dashboard page)
- [ ] Run typecheck: `npm run typecheck`
- [ ] Verify in browser with mobile device emulation

**Files:** `/frontend/src/components/common/MobileNotice.tsx` (new), `/frontend/src/components/Layout/Layout.tsx`

### US-009: Mobile sidebar behavior
- [ ] Update Sidebar.tsx to hide by default on mobile (<768px)
- [ ] Add hamburger menu button to mobile header/status bar
- [ ] Implement sidebar overlay with backdrop on mobile
- [ ] Add backdrop click handler to close sidebar
- [ ] Add navigation item click handler to close sidebar
- [ ] Set sidebar width to 80% screen width (max 280px) on mobile
- [ ] Ensure smooth animations on mobile
- [ ] Run typecheck: `npm run typecheck`
- [ ] Verify in browser with mobile device emulation

**Files:** `/frontend/src/components/Layout/Sidebar.tsx`, `/frontend/src/components/Layout/Layout.tsx`

---

## Phase 4: Testing & Validation (LOW PRIORITY)

### US-010: Test dashboard on real mobile devices
- [ ] Test dashboard on iOS Safari (iPhone 12+)
- [ ] Test dashboard on Android Chrome (Android 10+)
- [ ] Verify all tiles render correctly on both platforms
- [ ] Verify touch interactions are smooth (no delays)
- [ ] Check for console errors on mobile browsers
- [ ] Take screenshots for PR documentation
- [ ] Document any device-specific issues found

**Devices:** Real iOS and Android devices (not just emulation)

---

## Additional Tasks

### Unit/Integration Tests
- [ ] Add tests for useMediaQuery hook
- [ ] Add tests for mobile grid layout rendering
- [ ] Add tests for conditional drag-and-drop rendering
- [ ] Add tests for MobileNotice component

### E2E Tests (Playwright)
- [ ] Add E2E test for dashboard on mobile viewport (375px)
- [ ] Add E2E test for sidebar overlay on mobile
- [ ] Add E2E test for mobile notice on non-dashboard pages
- [ ] Run tests: `./scripts/run-playwright-tests.sh core`

### Documentation
- [ ] Update `/docs/design/layout_definition.md` with mobile specifications
- [ ] Add mobile screenshots to documentation
- [ ] Update README with mobile support information

---

## Completed
- [x] PRD created: `tasks/prd-mobile-dashboard-support.md`
- [x] Ralph fix plan updated

---

## Notes

**Critical Rules:**
- Mobile breakpoint: `<768px` (use `md:` prefix for desktop styles)
- Mobile-first approach: default styles for mobile, add `md:`, `lg:` prefixes for desktop
- All acceptance criteria must include "Verify in browser using dev-browser skill"
- Run `npm run typecheck` before marking any task complete
- Test on real devices in Phase 4 (not just browser emulation)

**Technical Approach:**
- Use Tailwind responsive classes (`hidden md:flex`, `text-2xl md:text-3xl`)
- Use `useMediaQuery` hook for conditional rendering in React
- Disable drag-and-drop on mobile to reduce JavaScript overhead
- Touch targets minimum 44px height (Apple HIG, Material Design)

**Success Criteria:**
- No horizontal scrolling on 320px-428px screens
- Touch responses within 100ms
- Lighthouse mobile score ≥90 (Performance), ≥95 (Accessibility)
- All tiles render correctly on iOS Safari and Android Chrome

**Design Alignment:**
- Follow `/docs/design/ui_design_document.md` for colors, spacing, typography
- Follow `/docs/design/layout_definition.md` for layout structure
- Maintain consistency with existing desktop experience

**Out of Scope:**
- Tablet-specific layouts (tablets use desktop layout)
- Full-app mobile optimization (only dashboard)
- PWA/offline features
- Touch gestures (swipe, pinch-to-zoom, pull-to-refresh)
- Mobile-specific tile types or visualizations
