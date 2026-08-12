// Our branch palette and card colours expressed as mind-elixir themes. The values
// come from the "Anela — otevřená témata" template: warm paper ground, near-black
// inverted root in light mode, near-white inverted root in dark mode.

import type { SubLineParams, Theme } from "mind-elixir";
import { MIND_MAP_PALETTE } from "./mindMapTheme";

/**
 * Horizontal gap between a node and its children. Shared between the `--node-gap-x`
 * CSS variable and {@link sideCentreBranch}, which has to know it — see below.
 */
const NODE_GAP_X = 12;

/**
 * Draws a sub-branch link as a cubic Bézier from the parent card's side centre to
 * the child card's, matching how mind-elixir already attaches top-level branches.
 *
 * Its own sub-branch style is a short curve running into a long horizontal line
 * beneath the child. That reads fine when deeper nodes are bare text, but every
 * node now carries a card (see mindMapCanvas.css), and against a card the
 * underline looks like a stray rule rather than a connector — the attachment point
 * visibly differs between a branch (side centre) and a leaf (underneath).
 *
 * The catch: both ends describe an <me-parent> box, and whether that box matches
 * the visible card depends on depth. Under <me-children>, <me-parent> carries
 * `padding: 6px var(--node-gap-x)`, so the box is a node gap wider than the card
 * on each side; a top-level branch's <me-parent> has no horizontal padding, so
 * there the box IS the card. Landing a curve on the raw box leaves it floating
 * beside the card, so each end is pulled in by NODE_GAP_X when — and only when —
 * that end is a deeper node.
 *
 * The child end always is. For the parent end, `isFirst` is the tell: mind-elixir
 * passes `true` only on the call it makes from linkDiv, whose parent is the
 * top-level branch, and passes nothing on the recursive calls that draw every
 * deeper link. Verified in a browser against the real library — neither the
 * padding asymmetry nor the meaning of `isFirst` is derivable from the types.
 */
function sideCentreBranch({ pT, pL, pW, pH, cT, cL, cW, cH, direction, isFirst }: SubLineParams): string {
  const isRight = direction === "rhs";
  const parentInset = isFirst ? 0 : NODE_GAP_X;
  const x1 = isRight ? pL + pW - parentInset : pL + parentInset;
  const x2 = isRight ? cL + NODE_GAP_X : cL + cW - NODE_GAP_X;
  const y1 = pT + pH / 2;
  const y2 = cT + cH / 2;
  const midX = (x1 + x2) / 2;
  return `M${x1},${y1} C${midX},${y1} ${midX},${y2} ${x2},${y2}`;
}

export const MIND_MAP_LIGHT_THEME: Theme = {
  name: "anela-light",
  type: "light",
  palette: [...MIND_MAP_PALETTE],
  generateSubBranch: sideCentreBranch,
  cssVar: {
    "--node-gap-x": `${NODE_GAP_X}px`,
    "--node-gap-y": "12px",
    "--main-gap-x": "36px",
    "--main-gap-y": "12px",
    "--main-color": "#2B2724",
    "--main-bgcolor": "#FFFFFF",
    "--color": "#2B2724",
    "--bgcolor": "#FAF8F5",
    "--selected": "#1F6FB2",
    "--root-color": "#FFFFFF",
    "--root-bgcolor": "#2B2724",
    "--root-border-color": "#2B2724",
    "--root-radius": "14px",
    "--main-radius": "9px",
    "--topic-padding": "7px 13px",
  },
};

export const MIND_MAP_DARK_THEME: Theme = {
  name: "anela-dark",
  type: "dark",
  palette: [...MIND_MAP_PALETTE],
  generateSubBranch: sideCentreBranch,
  cssVar: {
    "--node-gap-x": `${NODE_GAP_X}px`,
    "--node-gap-y": "12px",
    "--main-gap-x": "36px",
    "--main-gap-y": "12px",
    "--main-color": "#E6E3DF",
    "--main-bgcolor": "#1B1C1F",
    "--color": "#E6E3DF",
    "--bgcolor": "#141517",
    "--selected": "#4FA3E3",
    "--root-color": "#2B2724",
    "--root-bgcolor": "#EDE7DF",
    "--root-border-color": "#EDE7DF",
    "--root-radius": "14px",
    "--main-radius": "9px",
    "--topic-padding": "7px 13px",
  },
};

export function themeFor(mode: "light" | "dark"): Theme {
  return mode === "dark" ? MIND_MAP_DARK_THEME : MIND_MAP_LIGHT_THEME;
}
