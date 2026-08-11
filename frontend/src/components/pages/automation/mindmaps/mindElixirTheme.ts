// Our branch palette and card colours expressed as mind-elixir themes. The values
// come from the "Anela — otevřená témata" template: warm paper ground, near-black
// inverted root in light mode, near-white inverted root in dark mode.

import type { Theme } from "mind-elixir";
import { MIND_MAP_PALETTE } from "./mindMapTheme";

export const MIND_MAP_LIGHT_THEME: Theme = {
  name: "anela-light",
  type: "light",
  palette: [...MIND_MAP_PALETTE],
  cssVar: {
    "--node-gap-x": "12px",
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
  cssVar: {
    "--node-gap-x": "12px",
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
