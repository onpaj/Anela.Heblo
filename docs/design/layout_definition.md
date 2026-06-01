# 📐 Application Layout Definition

This document defines the complete layout structure and positioning of UI elements for the Anela Heblo application across desktop and mobile devices.

---

## 1. 🖥️ Desktop Layout Structure

### Application Shell Layout
```
┌─────────────────────────────────────────────────────────────┐
│[◄►]    │                                                    │
│        │              Main Content Area                     │
│Anela   │                                                    │
│Heblo   │         Pages, Components, Forms                   │
│        │                                                    │
│Nav     │                                                    │
│Items   │                                                    │
│        │                                                    │
│        │                                                    │
│        │                                                    │
│        │                                                    │
│        │                                                    │
├────────┼─────────────────────────────────────────────────────┤
│[👤]    │ Anela Heblo v0.1.0  Development  Mock Auth  API:  │ ← Status Bar
│User    │                                            localhost │  (24px height)
└────────┴─────────────────────────────────────────────────────┘
```

### Layout Elements Positioning

#### 1. **Sidebar (Navigation)**
- **Position**: `fixed left-0 top-0 bottom-0` (full height)
- **Z-index**: `z-40`
- **States**:
  - **Expanded**: `w-64` (256px width)
  - **Collapsed**: `w-16` (64px width)
- **Behavior**:
  - **Auto-expand on navigation**: Clicking any menu item in collapsed state automatically expands sidebar
  - **Manual toggle**: Users can manually collapse/expand using toggle button
- **Structure**:
  ```
  Expanded (256px):
  ┌─────────────────────────┐
  │ Anela Heblo             │ ← App title at top
  │ 🏠 Dashboard            │
  │ 📊 Analytics            │
  │ 🛍️ Katalog              │
  │ 🏭 Výroba               │
  │ 🚚 Doprava              │
  │ 📋 Nákup                │
  │ 🧾 Faktury              │
  │                         │
  │        (flex-grow)      │
  │ ┌─────────────────────┐ │
  │ │ [👤] Jan Novák  [◄] │ │ ← User + Toggle at bottom
  │ └─────────────────────┘ │
  └─────────────────────────┘
  
  Collapsed (64px):
  ┌─────┐
  │  A  │ ← App logo/initial
  │ 🏠  │
  │ 📊  │
  │ 🛍️  │
  │ 🏭  │
  │ 🚚  │
  │ 📋  │
  │ 🧾  │
  │     │
  │[👤]│ ← User icon
  │ [►] │ ← Toggle button
  └─────┘
  ```

#### 2. **Main Content Area**
- **Position**: `ml-64` (expanded sidebar) or `ml-16` (collapsed sidebar)
- **Top Offset**: No top offset needed (no topbar)
- **Bottom Offset**: `mb-6` (above 24px status bar - small margin only)
- **Padding**: `p-6` (24px all sides)
- **Max Width**: **REMOVED** - Content now uses full available width for optimal use of screen real estate on large monitors

#### 3. **Status Bar**
- **Position**: `fixed bottom-0 left-64 right-0 z-10` (beside sidebar, not full width)
- **Position when sidebar collapsed**: `fixed bottom-0 left-16 right-0 z-10`
- **Height**: `24px` (6 Tailwind units, not 32px)
- **Behavior**: 
  - Positioned **beside the sidebar**, not full width
  - Sidebar extends to bottom, status bar is **to the right** of sidebar
  - Never overlaps application content
  - Main content has NO bottom padding (status bar doesn't interfere)
- **Content** (from left to right as shown in image):
  - **Version**: "v0.1.0"
  - **Environment**: "Development"
  - **Auth mode**: "Mock Auth" in case of mock authentication, otherwise empty
  - **API endpoint**: "API: localhost:5001"
- **Visual Design**:
  - Background: Light gray/white `bg-gray-100 border-t border-gray-200`
  - Text: Small, subdued `text-xs text-gray-600`
  - Padding: `px-4 py-1` (16px horizontal, 4px vertical)
  - Data in status bar should be aligned to the right
  - Items in status bar should be visually separated by `|` or similar
- ** Color scheme**:
  - **Development**: red background, black text
  - **Test**: green background, white text
  - **Production**: default background, primary text color
  - **Mock Auth**: when auth is mocked, show "Mock Auth" badge in warning colors in status bar
---

## 2. 📱 Mobile Layout Structure

### Mobile Application Shell
```
┌─────────────────────────────────┐
│                                 │
│                                 │
│         Main Content            │
│        (Full Width)             │
│                                 │
│                                 │
│                                 │
│                                 │
│                                 │
├─────────────────────────────────┤
│ Anela Heblo v0.1.0  Dev  Mock  │ ← Status Bar (24px, full width on mobile)
└─────────────────────────────────┘

[Sidebar Overlay - Hidden by default, triggered by swipe or floating button]
┌─────────────────┐
│ Anela Heblo     │ ← App title at top
│ 🏠 Dashboard    │
│ 📊 Analytics    │
│ 🛍️ Katalog      │
│ 🏭 Výroba       │
│ 🚚 Doprava      │
│ 📋 Nákup        │
│ 🧾 Faktury      │
│                 │
│                 │
│ [👤] Jan Novák  │ ← User info at bottom
└─────────────────┘
```

### Mobile Layout Elements

#### 1. **Mobile Sidebar (Overlay)**
- **Position**: `fixed inset-y-0 left-0 z-50`
- **Width**: `w-64` (256px) - same as desktop expanded
- **Behavior**: 
  - Hidden by default (`-translate-x-full`)
  - Slides in when triggered by swipe gesture or floating menu button
  - Backdrop overlay with `bg-black/50`
  - Contains app title at top and user info at bottom
- **Animation**: `transition-transform duration-300`
- **Structure**:
  ```
  ┌─────────────────────────┐
  │ Anela Heblo             │ ← App title at top
  │ 🏠 Dashboard            │
  │ 📊 Analytics            │
  │ 🛍️ Katalog              │
  │ 🏭 Výroba               │
  │ 🚚 Doprava              │
  │ 📋 Nákup                │
  │ 🧾 Faktury              │
  │                         │
  │        (flex-grow)      │
  │ [👤] Jan Novák          │ ← User info at bottom
  └─────────────────────────┘
  ```

#### 2. **Mobile Main Content**
- **Position**: Full width `w-full`
- **Top Offset**: No top offset (no topbar)
- **Bottom Offset**: `mb-6` (above 24px status bar - small margin only)
- **Padding**: Reduced padding `p-4` (16px)
- **No left margin** (sidebar is overlay, not fixed)

#### 3. **Mobile Status Bar**
- **Position**: `fixed bottom-0 left-0 right-0 z-10` (full width on mobile)
- **Height**: `24px` (6 Tailwind units)
- **Content** (condensed for mobile):
  - **Left**: "Anela Heblo v0.1.0"
  - **Center**: "Dev" (shortened environment)
  - **Right**: "Mock" (shortened auth mode)
- **Visual Design**: Same as desktop - light gray background, small text

---

## 3. 🎯 Layout Component Specifications

### Element Hierarchy & Z-Index
```
z-50: Mobile sidebar overlay (highest)
z-40: Sidebar (desktop fixed)  
z-30: Status Bar + backdrop
z-20: Modal dialogs
z-10: Dropdown menus, tooltips
z-0:  Main content (lowest)
```

### Navigation Items Layout
```
Each nav item (48px height):
┌─────────────────────────────┐
│ [📊] Analytics          [>] │ ← Expanded: icon + text + arrow
│  16px   flex-1         16px │
└─────────────────────────────┘

┌─────┐
│ 📊  │ ← Collapsed: icon only, centered
│     │    Click triggers: navigation + auto-expand
└─────┘
```

**Navigation Behavior**:
- **Expanded state**: Normal navigation - click navigates to page
- **Collapsed state**: Click performs dual action:
  1. Navigates to the selected page/route
  2. Automatically expands sidebar to show full navigation
- **Toggle button**: Independent control for manual expand/collapse
- **Responsive**: On tablet/desktop only (mobile uses overlay)

### Sidebar Bottom Component Layout
```
Expanded sidebar (bottom):
┌─────────────────────────┐
│                         │
│ [👤] Jan Novák     [◄] │ ← User info + collapse button at bottom
└─────────────────────────┘

Collapsed sidebar (bottom):
┌─────┐
│     │
│[👤] │ ← User icon
│ [►] │ ← Expand button
└─────┘
```

### Status Bar Layout
```
Desktop Status Bar (full width, 32px height):
┌─────────────────────────────────────────────────────────────┐
│ v1.2.3    [TEST] Development Environment    Connected ✓    │
│ Left      Center (with colored badge)        Right         │
└─────────────────────────────────────────────────────────────┘

Mobile Status Bar (condensed, 32px height):  
┌─────────────────────────────────┐
│ v1.2.3  [TEST]    Connected ✓  │
│ Left    Center     Right       │
└─────────────────────────────────┘

Environment Badge Colors:
• Development: [DEV] - Yellow background, black text
• Test:        [TEST] - Blue background, white text  
• Production:  [PROD] - Green background, white text
```

### User Profile Menu Layout
```
Desktop (in sidebar bottom, left side):
┌─────────────────────────────┐
│ [👤] Jan Novák        [▼]  │ ← User dropdown menu in sidebar bottom
└─────────────────────────────┘

When clicked - dropdown menu (above user area):
┌─────────────────────────────┐
│ Profile Settings            │
├─────────────────────────────┤
│ Sign out                    │ 
└─────────────────────────────┘

Mobile (in sidebar overlay bottom):
┌─────────────────────────────┐
│ [👤] Jan Novák        [▼]  │ ← User info in sidebar overlay
└─────────────────────────────┘
```

---

## 4. 📐 Responsive Breakpoints & Behavior

### Breakpoint Definitions
- **Mobile**: `< 768px` (sm breakpoint)
- **Tablet**: `768px - 1024px` (md to lg)
- **Desktop**: `≥ 1024px` (lg breakpoint)

### Layout Transitions
```css
/* Mobile → Tablet */
@media (min-width: 768px) {
  .sidebar-overlay { display: none; }
  .sidebar-fixed { display: block; }
  .main-content { margin-left: 64px; } /* Collapsed by default */
}

/* Tablet → Desktop */  
@media (min-width: 1024px) {
  .main-content { margin-left: 256px; } /* Expanded by default */
}
```

### Sidebar Interaction Behavior
- **Desktop/Tablet**: 
  - Clicking collapsed menu items triggers navigation + auto-expand
  - Toggle button provides manual control
  - Sidebar state persists across page navigation
- **Mobile**: 
  - Overlay sidebar closes after navigation
  - No auto-expand behavior (not applicable to overlay)

### Element Visibility Rules
| Element | Mobile | Tablet | Desktop |
|---------|--------|--------|---------|
| App Title | ✅ In Sidebar | ✅ In Sidebar | ✅ In Sidebar |
| Sidebar Fixed | ❌ Hidden | ✅ Visible | ✅ Visible |
| Sidebar Overlay | ✅ Available | ❌ Hidden | ❌ Hidden |
| User Profile Menu | ✅ In Sidebar | ✅ In Sidebar | ✅ In Sidebar |
| Sidebar Toggle | ❌ Hidden | ✅ In Sidebar | ✅ In Sidebar |

---

## 5. 🎨 Layout Styling Specifications

### Colors & Visual Hierarchy
- **Sidebar**: `bg-white border-r border-gray-200 shadow-sm`
- **Main Content**: `bg-gray-50` (light background, no white container)
- **Active Nav Item**: `bg-blue-50 text-blue-600 border-r-2 border-blue-600`
- **App Title**: `text-lg font-semibold text-gray-900`
- **User Area**: `border-t border-gray-200` (top border separation)

### Spacing & Dimensions
- **Sidebar Width**: `256px` expanded (w-64), `64px` collapsed (w-16)
- **Content Padding**: Desktop `16px` (p-4), Mobile `12px` (p-3) - direct on background
- **Nav Item Height**: `48px` (h-12)
- **Icon Size**: `20px` (w-5 h-5) in navigation
- **App Title Height**: `64px` (h-16) at top of sidebar
- **User Area Height**: `64px` (h-16) at bottom of sidebar

### Page Layout Structure Rules

**MANDATORY: All pages MUST follow this standardized structure for consistency:**

#### Standard Page Container Pattern
```tsx
<div className="flex flex-col h-full w-full">
  {/* Header - Fixed */}
  <div className="flex-shrink-0 mb-3">
    <h1 className="text-lg font-semibold text-gray-900">Page Title</h1>
  </div>

  {/* Filters/Controls - Fixed (optional) */}
  <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
    {/* Filter components */}
  </div>

  {/* Main Content - Scrollable */}
  <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
    {/* Scrollable content area */}
  </div>
</div>
```

#### Container Class Requirements
- **Main Container**: `flex flex-col h-full w-full` - Full height and width flex container for responsive layout
- **Header Section**: `flex-shrink-0 mb-3` - Fixed header with 12px bottom margin
- **Filter Section**: `flex-shrink-0 mb-4` - Fixed filters with 16px bottom margin
- **Content Section**: `flex-1 min-h-0` - Flexible scrollable content area

#### Spacing Standards
- **Header Margin**: `mb-3` (12px) - Consistent across all pages
- **Filter Margin**: `mb-4` (16px) - When filters are present
- **Filter Padding**: `p-4` (16px) - Internal filter container padding
- **NO additional padding**: Main container has no internal padding - content uses Layout padding only

#### Background & Styling
- **Main Background**: Inherit from Layout (`bg-gray-50`)
- **Content Containers**: `bg-white shadow rounded-lg` - White cards with subtle shadow
- **Headers**: `text-lg font-semibold text-gray-900` - Consistent typography

#### Responsive Width Requirements
**CRITICAL**: All page components MUST use full available width for optimal large monitor support:

- **Layout Container**: Uses `w-full` instead of `max-w-7xl mx-auto` - no width restrictions in main layout
- **Page Components**: MUST include `w-full` class in root container: `<div className="flex flex-col h-full w-full">`
- **Content Expansion**: All tables, forms, and content areas should utilize full available width
- **Large Monitor Optimization**: Content expands to fill ultra-wide monitors (>1920px)

#### Common Anti-Patterns to AVOID
- ❌ **Double padding**: Never add `px-4 py-6` or similar to main page container
- ❌ **Inconsistent margins**: Always use `mb-3` for headers, `mb-4` for filters
- ❌ **Fixed height content**: Use `flex-1 min-h-0` for scrollable areas
- ❌ **Missing container structure**: Always use the three-tier structure (header/filters/content)
- ❌ **Fixed width containers**: Never use `max-w-7xl mx-auto` or similar width restrictions on page components
- ❌ **Missing `w-full` class**: All page root containers must include `w-full` for responsive expansion

### Animation Specifications
- **Sidebar Toggle**: `transition-all duration-300 ease-in-out`
- **Mobile Sidebar**: `transition-transform duration-300 ease-in-out`
- **Hover States**: `transition-colors duration-150 ease-in-out`

---

## Summary

This layout definition establishes the precise positioning and behavior of all UI elements:

- **Sidebar**: Full-height collapsible navigation (fixed on desktop, overlay on mobile) containing app title, navigation, user info, and toggle button
- **Main Content**: Responsive content area that adapts to sidebar state, no top offset needed
- **User Profile**: Located in sidebar bottom with dropdown menu for all screen sizes
- **Mobile Adaptations**: Overlay sidebar triggered by swipe gesture or floating button

Key changes from previous layout:
- **Topbar completely removed** - no header bar at top of application
- **App title moved to sidebar top** - "Anela Heblo" displayed at top of sidebar
- **User profile/login moved to sidebar bottom** - replaces previous topbar location
- **Main content extends to full height** - no top margin/offset needed
- **Sidebar contains all UI elements**: app title, navigation, user info, and toggle button
- **Mobile sidebar overlay** includes app title and user info, same as desktop
- Auto-expand behavior: clicking menu items in collapsed sidebar expands it

All measurements use Tailwind CSS units for consistent implementation across the application.