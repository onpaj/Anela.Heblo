## Module
Article

## Finding
`HtmlContent` in `frontend/src/features/articles/ArticleDetail.tsx` (lines 13–15) reads the current theme at render time by querying the DOM directly:

```tsx
const isDark = document.documentElement.classList.contains('dark');
```

This value is **not reactive state** — it is a plain boolean captured once at render time. The project's `ThemeContext` (`frontend/src/contexts/ThemeContext.tsx`) exposes a `useTheme()` hook that returns a reactive `theme` value backed by React state. Reading the DOM class list instead of calling `useTheme()` means the component never subscribes to theme changes.

The `key={isDark ? 'dark' : 'light'}` on the `<iframe>` (line 23) is meant to force a remount (and thus a re-render of the inline `srcDoc` styles) when the theme changes, but since `isDark` is captured once at initial render and never recomputed on re-render, the key never changes and the iframe keeps its stale theme colors after the user toggles the theme while an article panel is open.

## Reference (read directly from source during triage)
```tsx
function HtmlContent({ html }: { html: string }) {
  const isDark = document.documentElement.classList.contains('dark');
  const srcdoc = `<!DOCTYPE html><html><head><meta charset="utf-8"><style>
    body{font-family:system-ui,sans-serif;line-height:1.6;color:${isDark ? '#E6E8EC' : '#1f2937'};background:${isDark ? '#202327' : 'transparent'};padding:1rem;margin:0}
    h1,h2,h3{color:${isDark ? '#E6E8EC' : '#111827'}}p{margin:0 0 1em}ul,ol{padding-left:1.5em}
    a{color:${isDark ? '#38BDF8' : '#2563eb'}}
  </style></head><body>${html}</body></html>`;

  return (
    <iframe
      key={isDark ? 'dark' : 'light'}
      srcDoc={srcdoc}
      sandbox="allow-same-origin"
      className="w-full border-0 rounded"
      style={{ minHeight: '500px' }}
      onLoad={(e) => {
        const iframe = e.currentTarget;
        const body = iframe.contentDocument?.body;
        if (body) {
          iframe.style.height = `${body.scrollHeight + 32}px`;
        }
      }}
      title="Obsah článku"
    />
  );
}
```

## Recommended solution (from the finding)
Replace the DOM query with the `useTheme()` hook so `isDark` becomes reactive state:

```tsx
const { theme } = useTheme();
const isDark = theme === 'dark';
```

This makes `isDark` reactive, so the component re-renders and the `key` changes (remounting the iframe with corrected inline styles) whenever the theme changes — including while an article detail panel is already open.
