// Visual vocabulary of the mind map canvas, ported from the "Anela — otevřená témata"
// HTML template. Kept free of React so both the layout engine and the node components
// can share one source of truth for fonts, sizes and colours.

/** Branch palette. Top-level branches are coloured by their index in this list. */
export const MIND_MAP_PALETTE = [
  "#2E7D6B",
  "#1F6FB2",
  "#6A4C93",
  "#B5651D",
  "#C2185B",
  "#7A8B2F",
  "#D08700",
  "#4C7A34",
  "#3D7B8C",
  "#77706A",
] as const;

/** Used for the root's own edges and for anything that has no branch ancestor. */
export const NEUTRAL_BRANCH_COLOR = "#77706A";

// The layout measures text with these exact fonts, and MindMapFlowNode renders with
// them, so measured geometry and painted geometry always agree. Changing one without
// the other makes cards overlap.
export const MIND_MAP_FONT_FAMILY =
  '-apple-system, BlinkMacSystemFont, "Segoe UI", Inter, "Helvetica Neue", Arial, sans-serif';

export type NodeTier = "root" | "branch" | "leaf";

export interface TierMetrics {
  fontSize: number;
  fontWeight: number;
  paddingX: number;
  paddingY: number;
  borderWidth: number;
  lineHeight: number;
}

const LINE_HEIGHT_RATIO = 1.3;

function tier(fontSize: number, fontWeight: number, paddingX: number, paddingY: number, borderWidth: number): TierMetrics {
  return {
    fontSize,
    fontWeight,
    paddingX,
    paddingY,
    borderWidth,
    lineHeight: Math.round(fontSize * LINE_HEIGHT_RATIO),
  };
}

export const TIER_METRICS: Record<NodeTier, TierMetrics> = {
  root: tier(19, 650, 22, 14, 1),
  branch: tier(15, 600, 13, 7, 1.5),
  leaf: tier(14, 400, 13, 7, 1),
};

export function tierOf(depth: number): NodeTier {
  if (depth === 0) return "root";
  if (depth === 1) return "branch";
  return "leaf";
}

export function fontOf(t: NodeTier): string {
  const m = TIER_METRICS[t];
  return `${m.fontWeight} ${m.fontSize}px ${MIND_MAP_FONT_FAMILY}`;
}

/** The short "note" badge rendered inline after the title. */
export const NOTE_BADGE_FONT = `700 10.5px ${MIND_MAP_FONT_FAMILY}`;
export const NOTE_BADGE_EXTRA_WIDTH = 20; // 2×6px padding + 8px left margin

/** The child-count pill shown on a collapsed node. */
export const COUNT_BADGE_FONT = `700 11px ${MIND_MAP_FONT_FAMILY}`;
export const COUNT_BADGE_EXTRA_WIDTH = 22; // 2×7px padding + 8px left margin

/** Lock glyph rendered next to the title of a locked node. */
export const LOCK_ICON_WIDTH = 18;

/** Card content never grows past this; longer text wraps. */
export const MAX_CONTENT_WIDTH = 560;

/** Vertical gap between siblings and horizontal gap between levels (template values). */
export const ROW_GAP = 12;
export const LEVEL_GAP = 62;

export function branchColorAt(branchIndex: number): string {
  return MIND_MAP_PALETTE[branchIndex % MIND_MAP_PALETTE.length];
}

/**
 * Mixes a hex colour toward white. The palette is tuned for the template's warm paper
 * background; on the dark theme the same values read as muddy, so branch text and
 * borders are lightened instead of swapping in a second palette.
 */
export function lightenColor(hex: string, amount: number): string {
  const match = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!match) return hex;
  const value = parseInt(match[1], 16);
  const mix = (channel: number) => Math.round(channel + (255 - channel) * amount);
  const r = mix((value >> 16) & 0xff);
  const g = mix((value >> 8) & 0xff);
  const b = mix(value & 0xff);
  return `#${((r << 16) | (g << 8) | b).toString(16).padStart(6, "0")}`;
}

export const DARK_THEME_LIGHTEN = 0.42;

/** Branch colour as it should be painted on the current theme. */
export function themedBranchColor(hex: string, isDark: boolean): string {
  return isDark ? lightenColor(hex, DARK_THEME_LIGHTEN) : hex;
}
