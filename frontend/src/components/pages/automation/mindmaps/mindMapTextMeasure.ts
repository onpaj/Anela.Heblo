// Text measurement for the mind map layout. Uses a canvas 2D context when the
// browser provides one, and falls back to a per-character estimate otherwise
// (jsdom has no canvas backend, so every test would otherwise measure 0 and the
// layout would collapse every card onto the same point).

export type MeasureText = (text: string, font: string) => number;

const FALLBACK_CHAR_WIDTH_RATIO = 0.52;
const DEFAULT_FONT_SIZE = 14;

function fontSizeOf(font: string): number {
  const match = /(\d+(?:\.\d+)?)px/.exec(font);
  return match ? Number(match[1]) : DEFAULT_FONT_SIZE;
}

export function estimateTextWidth(text: string, font: string): number {
  return text.length * fontSizeOf(font) * FALLBACK_CHAR_WIDTH_RATIO;
}

// `undefined` = not resolved yet, `null` = resolved and unavailable. Resolving once
// matters: layout runs on every document change and calls this thousands of times.
let cachedContext: CanvasRenderingContext2D | null | undefined;

function canvasContext(): CanvasRenderingContext2D | null {
  if (cachedContext !== undefined) return cachedContext;
  // jsdom has no canvas backend: calling getContext there returns null *and* logs a
  // "not implemented" error to the virtual console. Skip it under test and use the
  // estimator, which is what the fallback would do anyway — just without the noise.
  if (process.env.NODE_ENV === "test") {
    cachedContext = null;
    return cachedContext;
  }
  try {
    cachedContext = document.createElement("canvas").getContext("2d");
  } catch {
    cachedContext = null;
  }
  return cachedContext;
}

export const measureTextWidth: MeasureText = (text, font) => {
  const ctx = canvasContext();
  if (!ctx) return estimateTextWidth(text, font);
  ctx.font = font;
  const width = ctx.measureText(text).width;
  // jsdom's stub canvas (when one is installed) reports 0 for everything; a zero
  // width for non-empty text is never real, so fall back rather than collapse.
  return width > 0 || text.length === 0 ? width : estimateTextWidth(text, font);
};

/** Test seam: forget the resolved context so a suite can swap the canvas stub. */
export function resetMeasureCache(): void {
  cachedContext = undefined;
}
