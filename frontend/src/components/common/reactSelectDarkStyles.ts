import { StylesConfig, GroupBase } from "react-select";

/**
 * Theme-aware react-select styles shared by the autocomplete/combobox
 * components. The light branch preserves the existing look exactly; the dark
 * branch applies the Graphite dark-mode palette.
 *
 * Light values (defaults): border #d1d5db, focus #3b82f6, error #ef4444,
 * hover #9ca3af, white control/menu, min-height 38px, menu zIndex 50000.
 */

// Graphite dark-mode palette (mirrors tailwind.config.js `graphite` scale).
export const GRAPHITE = {
  surface: "#202327", // menu background
  surface2: "#272A30", // control background
  hover: "#2E323A", // focused option background
  border: "#2D3138",
  borderStrong: "#3C424B",
  text: "#E6E8EC",
  muted: "#9AA0AA",
  faint: "#6A707A",
  accent: "#38BDF8",
  accentInk: "#08171F",
} as const;

// Light palette (existing look).
const LIGHT = {
  control: "#ffffff",
  menu: "#ffffff",
  border: "#d1d5db",
  borderHover: "#9ca3af",
  focus: "#3b82f6",
  error: "#ef4444",
  optionSelected: "#3b82f6",
  optionSelectedText: "#ffffff",
  optionFocused: "#f3f4f6",
  optionText: "#1f2937",
  optionDefaultBg: "#ffffff",
  noOptions: "#6b7280",
} as const;

const MENU_Z_INDEX = 50000;

export interface SelectStyleOptions {
  /** Whether the field is in an error state. */
  error?: boolean;
}

/**
 * Returns a react-select StylesConfig themed for light or dark mode.
 *
 * @param isDark - true when the app is in dark mode (html.dark)
 * @param options - optional flags such as `error`
 */
export function getSelectStyles<
  Option = unknown,
  IsMulti extends boolean = false,
  Group extends GroupBase<Option> = GroupBase<Option>,
>(
  isDark: boolean,
  options: SelectStyleOptions = {},
): StylesConfig<Option, IsMulti, Group> {
  const { error = false } = options;

  return {
    control: (provided, state) => ({
      ...provided,
      backgroundColor: isDark ? GRAPHITE.surface2 : LIGHT.control,
      borderColor: error
        ? LIGHT.error
        : state.isFocused
          ? isDark
            ? GRAPHITE.accent
            : LIGHT.focus
          : isDark
            ? GRAPHITE.border
            : LIGHT.border,
      boxShadow: state.isFocused
        ? `0 0 0 1px ${isDark ? GRAPHITE.accent : LIGHT.focus}`
        : "none",
      "&:hover": {
        borderColor: error
          ? LIGHT.error
          : state.isFocused
            ? isDark
              ? GRAPHITE.accent
              : LIGHT.focus
            : isDark
              ? GRAPHITE.borderStrong
              : LIGHT.borderHover,
      },
      minHeight: "38px",
    }),
    option: (provided, state) => ({
      ...provided,
      backgroundColor: state.isSelected
        ? isDark
          ? GRAPHITE.accent
          : LIGHT.optionSelected
        : state.isFocused
          ? isDark
            ? GRAPHITE.hover
            : LIGHT.optionFocused
          : isDark
            ? GRAPHITE.surface
            : LIGHT.optionDefaultBg,
      color: state.isSelected
        ? isDark
          ? GRAPHITE.accentInk
          : LIGHT.optionSelectedText
        : isDark
          ? GRAPHITE.text
          : LIGHT.optionText,
      padding: "8px 12px",
    }),
    menu: (provided) => ({
      ...provided,
      backgroundColor: isDark ? GRAPHITE.surface : LIGHT.menu,
      zIndex: MENU_Z_INDEX,
    }),
    menuPortal: (provided) => ({
      ...provided,
      zIndex: MENU_Z_INDEX,
    }),
    singleValue: (provided) => ({
      ...provided,
      color: isDark ? GRAPHITE.text : provided.color,
    }),
    input: (provided) => ({
      ...provided,
      color: isDark ? GRAPHITE.text : provided.color,
    }),
    placeholder: (provided) => ({
      ...provided,
      color: isDark ? GRAPHITE.faint : provided.color,
    }),
    noOptionsMessage: (provided) => ({
      ...provided,
      color: isDark ? GRAPHITE.muted : LIGHT.noOptions,
      fontSize: "14px",
    }),
  };
}
