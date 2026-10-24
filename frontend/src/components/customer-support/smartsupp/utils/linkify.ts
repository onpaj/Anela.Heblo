type TextSegment =
  | { kind: "text"; text: string }
  | { kind: "link"; text: string; href: string };

const URL_PATTERN = /(?:https?:\/\/|www\.)[^\s<>]+/gi;
const SCHEME_PREFIX = /^(?:https?:\/\/|www\.)/i;
const TRAILING_PUNCTUATION = /[.,;:!?)\]}'"»]+$/;

export function isRenderableUrl(url: string | undefined, allowBlob = false): boolean {
  if (!url) return false;
  const lower = url.toLowerCase();
  return lower.startsWith("https://") || (allowBlob && lower.startsWith("blob:"));
}

function toHref(candidate: string): string {
  return candidate.toLowerCase().startsWith("www.") ? `https://${candidate}` : candidate;
}

export function splitLinks(text: string): TextSegment[] {
  const segments: TextSegment[] = [];
  let cursor = 0;

  for (const match of Array.from(text.matchAll(URL_PATTERN))) {
    const start = match.index ?? 0;
    const linkText = match[0].replace(TRAILING_PUNCTUATION, "");
    const href = toHref(linkText);
    // A bare scheme or "www." with nothing after it is not a link.
    if (linkText.replace(SCHEME_PREFIX, "") === "") continue;
    if (start > cursor) segments.push({ kind: "text", text: text.slice(cursor, start) });
    segments.push({ kind: "link", text: linkText, href });
    cursor = start + linkText.length;
  }

  if (cursor < text.length) segments.push({ kind: "text", text: text.slice(cursor) });
  return segments;
}
