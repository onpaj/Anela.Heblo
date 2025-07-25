# 📐 Application Layout Definition

This document defines the complete layout structure and positioning of UI elements for the Anela Heblo application across desktop and mobile devices.

---

## 1. 🖥️ Desktop Layout Structure

### Application Shell Layout
```
┌─────────────────────────────────────────────────────────────┐
│                    Topbar (64px height)                    │
│  [☰] Search [🔍]              [⚙️] [👤] UserMenu            │
├─────────────────────────────────────────────────────────────┤
│        │                                                    │
│        │              Main Content Area                     │
│Sidebar │                                                    │
│        │         Pages, Components, Forms                   │
│Nav     │                                                    │
│Items   │                                                    │
│        │                                                    │
│        │                                                    │
│[👤]    │                                                    │
│[◄►]    │                                                    │
├────────┼─────────────────────────────────────────────────────┤
│Status  │ Anela Heblo v0.1.0  Development  Mock Auth  API:  │ ← Status Bar
│Bar     │                                            localhost │  (24px height)
└────────┴─────────────────────────────────────────────────────┘
```

### Layout Elements Positioning

#### 1. **Topbar (Header)**
- **Position**: `fixed top-0 left-0 right-0 z-50`
- **Height**: `64px` (16 Tailwind units)
- **Structure**:
  ```
  ┌─────────────────────────────────────────────────────────┐
  │ [☰]  App Logo    [🔍 Search]    [⚙️] [🔔] [👤 Profile] │
  │ 16px              Flexible       Right-aligned  16px   │
  └─────────────────────────────────────────────────────────┘
  ```
- **Elements**:
  - **Mobile Menu Button**: `ml-4` (left: 16px) - only visible on mobile
  - **App Logo/Title**: `ml-4` when no mobile menu, `ml-2` when menu present
  - **Search Bar**: Center-left, expandable
  - **Actions Group**: Right-aligned with `mr-4` (right: 16px)
    - Settings icon `[⚙️]`
    - Notifications icon `[🔔]` (optional)
    - User profile dropdown `[👤]`

#### 2. **Sidebar (Navigation)**
- **Position**: `fixed left-0 top-16 bottom-0` (below topbar)
- **Z-index**: `z-40` (below topbar)
- **States**:
  - **Expanded**: `w-64` (256px width)
  - **Collapsed**: `w-16` (64px width)
- **Structure**:
  ```
  Expanded (256px):
  ┌─────────────────────────┐
  │ 🏠 Dashboard            │
  │ 📊 Analytics            │
  │ 🛍️ Katalog              │
  │ 🏭 Výroba               │
  │ 🚚 Doprava              │
  │ 📋 Nákup                │
  │ 🧾 Faktury              │
  │                         │
  │        (flex-grow)      │
  │                         │
  │ ┌─────────────────────┐ │
  │ │ [👤] Jan Novák      │ │
  │ │ Software Developer  │ │
  │ │              [◄]   │ │
  │ └─────────────────────┘ │
  └─────────────────────────┘
  
  Collapsed (64px):
  ┌─────┐
  │ 🏠  │
  │ 📊  │
  │ 🛍️  │
  │ 🏭  │
  │ 🚚  │
  │ 📋  │
  │ 🧾  │
  │     │
  │     │
  │ 👤  │
  │ [►] │
  └─────┘
  ```

#### 3. **Main Content Area**
- **Position**: `ml-64` (expanded sidebar) or `ml-16` (collapsed sidebar)
- **Top Offset**: `mt-16` (below 64px topbar)
- **Bottom Offset**: `mb-6` (above 24px status bar - small margin only)
- **Padding**: `p-6` (24px all sides)
- **Max Width**: `max-w-7xl mx-auto` (centered, max 1280px)

#### 4. **Status Bar**
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
│        Topbar (64px)            │
│ [☰] App Name        [👤] [⚙️]  │
├─────────────────────────────────┤
│                                 │
│                                 │
│         Main Content            │
│        (Full Width)             │
│                                 │
│                                 │
│                                 │
│                                 │
├─────────────────────────────────┤
│ Anela Heblo v0.1.0  Dev  Mock  │ ← Status Bar (24px, full width on mobile)
└─────────────────────────────────┘

[Sidebar Overlay - Hidden by default]
┌─────────────────┐
│ 🏠 Dashboard    │ ← Slide-in from left
│ 📊 Analytics    │   when hamburger tapped
│ 🛍️ Katalog      │
│ 🏭 Výroba       │
│ 🚚 Doprava      │
│ 📋 Nákup        │
│ 🧾 Faktury      │
│                 │
│ [👤] Jan Novák  │
│ Logout          │
└─────────────────┘
```

### Mobile Layout Elements

#### 1. **Mobile Topbar**
- **Height**: Same as desktop `64px`
- **Structure**:
  ```
  ┌─────────────────────────────────────┐
  │ [☰] App Name       [🔔] [👤] [⚙️] │
  │ 16px  Flexible      Right-aligned  │
  └─────────────────────────────────────┘
  ```
- **Elements**:
  - **Hamburger Menu**: `ml-4` - opens sidebar overlay
  - **App Title**: Centered or left-aligned after hamburger
  - **Actions**: Condensed, only essential icons

#### 2. **Mobile Sidebar (Overlay)**
- **Position**: `fixed inset-y-0 left-0 z-50`
- **Width**: `w-64` (256px) - same as desktop expanded
- **Behavior**: 
  - Hidden by default (`-translate-x-full`)
  - Slides in when hamburger is tapped (`translate-x-0`)
  - Backdrop overlay with `bg-black/50`
- **Animation**: `transition-transform duration-300`

#### 3. **Mobile Main Content**
- **Position**: Full width `w-full`
- **Top Offset**: `mt-16` (below topbar)
- **Bottom Offset**: `mb-6` (above 24px status bar - small margin only)
- **Padding**: Reduced padding `p-4` (16px)
- **No left margin** (sidebar is overlay, not fixed)

#### 4. **Mobile Status Bar**
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
z-50: Topbar (highest)
z-40: Sidebar (desktop fixed)  
z-30: Status Bar + Mobile sidebar overlay + backdrop
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
│     │
└─────┘
```

### User Profile Component Layout
```
Expanded sidebar (bottom):
┌─────────────────────────┐
│ ┌─────┐ Jan Novák   [◄] │
│ │ 👤  │ Developer        │ ← 48px height
│ └─────┘              │  │
└─────────────────────────┘

Collapsed sidebar (bottom):
┌─────┐
│ 👤  │ ← 48px height
│ [►] │
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

### Search Bar Layout
```
Desktop (in topbar):
┌───────────────────────────────┐
│ [🔍] Search anything...    [×] │ ← Expandable, max 400px
└───────────────────────────────┘

Mobile (full-screen overlay when focused):
┌─────────────────────────────────┐
│ [←] Search...              [×] │ ← Full screen overlay
├─────────────────────────────────┤
│ Recent searches:                │
│ • Dashboard                     │
│ • Analytics                     │
└─────────────────────────────────┘
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

### Element Visibility Rules
| Element | Mobile | Tablet | Desktop |
|---------|--------|--------|---------|
| Hamburger Menu | ✅ Visible | ❌ Hidden | ❌ Hidden |
| Sidebar Fixed | ❌ Hidden | ✅ Visible | ✅ Visible |
| Sidebar Overlay | ✅ Available | ❌ Hidden | ❌ Hidden |
| Search (Full) | ✅ Overlay | ✅ Inline | ✅ Inline |
| User Profile (Full) | ✅ In Sidebar | ✅ In Sidebar | ✅ In Sidebar |

---

## 5. 🎨 Layout Styling Specifications

### Colors & Visual Hierarchy
- **Topbar**: `bg-white border-b border-gray-200 shadow-sm`
- **Sidebar**: `bg-white border-r border-gray-200 shadow-sm`
- **Main Content**: `bg-gray-50` (light background)
- **Active Nav Item**: `bg-blue-50 text-blue-600 border-r-2 border-blue-600`

### Spacing & Dimensions
- **Topbar Height**: `64px` (h-16)
- **Sidebar Width**: `256px` expanded (w-64), `64px` collapsed (w-16)
- **Content Padding**: Desktop `24px` (p-6), Mobile `16px` (p-4)
- **Nav Item Height**: `48px` (h-12)
- **Icon Size**: `20px` (w-5 h-5) in navigation

### Animation Specifications
- **Sidebar Toggle**: `transition-all duration-300 ease-in-out`
- **Mobile Sidebar**: `transition-transform duration-300 ease-in-out`
- **Hover States**: `transition-colors duration-150 ease-in-out`

---

## Summary

This layout definition establishes the precise positioning and behavior of all UI elements:

- **Topbar**: Fixed header with navigation and user actions
- **Sidebar**: Collapsible navigation (fixed on desktop, overlay on mobile)  
- **Main Content**: Responsive content area that adapts to sidebar state
- **User Profile**: Context-aware profile display in sidebar
- **Mobile Adaptations**: Full-screen overlays and touch-optimized layouts

All measurements use Tailwind CSS units for consistent implementation across the application.