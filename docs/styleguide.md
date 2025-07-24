Design System for Cosmetics ERP Platform
Color Palette
Primary Colors

Primary White - #FFFFFF (Clean surfaces, main backgrounds, and container fills)
Primary Blue - #2563EB (Primary brand color for CTAs, navigation, and key interactions)
Neutral Slate - #0F172A (Primary text color and high-contrast elements)

Secondary Colors

Secondary Blue Light - #3B82F6 (Hover states, secondary buttons, and interactive elements)
Secondary Blue Pale - #EFF6FF (Subtle backgrounds, selected states, and content areas)
Neutral Gray - #64748B (Secondary text, labels, and subdued content)

Accent Colors

Accent Blue Bright - #1D4ED8 (Important actions, notifications, and status indicators)
Success Green - #10B981 (Confirmation states, success messages, and positive indicators)
Warning Amber - #F59E0B (Caution states, pending actions, and attention-drawing elements)
Error Red - #EF4444 (Error states, destructive actions, and critical alerts)

Functional Colors

Info Blue - #06B6D4 (Informational messages and neutral notifications)
Border Light - #E2E8F0 (Subtle borders, dividers, and container outlines)
Background Gray - #F8FAFC (Page backgrounds and section separators)
Disabled Gray - #94A3B8 (Disabled states and inactive elements)

Background Colors

Surface White - #FFFFFF (Cards, modals, and elevated surfaces)
Background Neutral - #F1F5F9 (Main application background)
Background Subtle - #F8FAFC (Secondary backgrounds and content areas)

Typography System
Font Families

Primary: Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif
Monospace: 'JetBrains Mono', 'SF Mono', Consolas, monospace (For data tables and technical content)

Font Weights

Light: 300 (Large headlines and decorative text)
Regular: 400 (Body text and standard content)
Medium: 500 (Subheadings and emphasized content)
Semibold: 600 (Button text and important labels)
Bold: 700 (Headings and primary navigation)

Typography Scale

Display: 32px / 2rem - Bold (Page titles and primary headlines)
H1: 28px / 1.75rem - Bold (Section headers)
H2: 24px / 1.5rem - Semibold (Subsection headers)
H3: 20px / 1.25rem - Semibold (Card titles and form sections)
H4: 18px / 1.125rem - Medium (Table headers and labels)
Body Large: 16px / 1rem - Regular (Primary body text)
Body: 14px / 0.875rem - Regular (Secondary body text and descriptions)
Body Small: 12px / 0.75rem - Medium (Captions and metadata)

Component Styling
Button Styling
css/* Primary Button */
background: #2563EB
color: #FFFFFF
padding: 12px 24px
border-radius: 8px
font-weight: 600
font-size: 14px
transition: all 150ms ease-in-out

/* Primary Hover */
background: #1D4ED8
transform: translateY(-1px)
box-shadow: 0 4px 12px rgba(37, 99, 235, 0.25)

/* Secondary Button */
background: transparent
color: #2563EB
border: 1px solid #E2E8F0
padding: 12px 24px
border-radius: 8px

/* Ghost Button */
background: transparent
color: #64748B
padding: 8px 16px
border-radius: 6px
Card Styling
cssbackground: #FFFFFF
border: 1px solid #E2E8F0
border-radius: 12px
padding: 24px
box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05)
transition: box-shadow 200ms ease-in-out

/* Card Hover */
box-shadow: 0 4px 20px rgba(0, 0, 0, 0.08)
border-color: #CBD5E1
Input Styling
cssbackground: #FFFFFF
border: 1px solid #E2E8F0
border-radius: 8px
padding: 12px 16px
font-size: 14px
color: #0F172A
transition: border-color 150ms ease-in-out

/* Input Focus */
border-color: #2563EB
box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.1)
outline: none

/* Input Error */
border-color: #EF4444
box-shadow: 0 0 0 3px rgba(239, 68, 68, 0.1)
Iconography

Style: Lucide React (consistent, minimal, 24px default size)
Weight: 1.5px stroke width for optimal clarity
Colors: Inherit from parent or use #64748B for neutral states
Interactive Icons: Scale to 20px with #2563EB color on hover

Spacing System

xs: 4px (Fine-tuned spacing between related elements)
sm: 8px (Close element relationships)
md: 16px (Standard component spacing)
lg: 24px (Section spacing and card padding)
xl: 32px (Major section separators)
2xl: 48px (Page-level spacing)
3xl: 64px (Hero sections and major content blocks)

Motion & Animation
Timing Functions

Ease In Out: cubic-bezier(0.4, 0, 0.2, 1) (Standard transitions)
Ease Out: cubic-bezier(0, 0, 0.2, 1) (Entrance animations)
Ease In: cubic-bezier(0.4, 0, 1, 1) (Exit animations)

Duration Scale

Fast: 150ms (Micro-interactions, button states)
Base: 200ms (Standard component transitions)
Slow: 300ms (Page transitions, modal animations)
Slower: 500ms (Complex state changes)

Animation Patterns

Hover Elevation: transform: translateY(-2px) with shadow increase
Button Press: transform: scale(0.98) for tactile feedback
Modal Entry: Fade in with transform: scale(0.95) to scale(1)
Loading States: Subtle pulse animation with opacity 0.6-1.0

Layout Principles

Max Content Width: 1200px with centered alignment
Grid System: 12-column responsive grid with 24px gutters
Vertical Rhythm: 24px baseline for consistent content flow
Responsive Breakpoints:

Mobile: 320px+
Tablet: 768px+
Desktop: 1024px+
Large: 1200px+



This design system prioritizes clarity and efficiency for logistics and manufacturing workflows while maintaining the clean, tech-forward aesthetic with strategic blue accents throughout the cosmetics ERP platform.