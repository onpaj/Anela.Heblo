/**
 * Fixed palette for multi-product chart series. Colors are assigned by the product's
 * index in the current selection, so a product keeps its color as long as its position
 * holds — the backend returns series in the order they were requested.
 */
const SERIES_COLORS: ReadonlyArray<{ border: string; background: string }> = [
  { border: "rgba(59, 130, 246, 1)", background: "rgba(59, 130, 246, 0.15)" }, // blue
  { border: "rgba(34, 197, 94, 1)", background: "rgba(34, 197, 94, 0.15)" }, // green
  { border: "rgba(168, 85, 247, 1)", background: "rgba(168, 85, 247, 0.15)" }, // purple
  { border: "rgba(251, 146, 60, 1)", background: "rgba(251, 146, 60, 0.15)" }, // orange
  { border: "rgba(236, 72, 153, 1)", background: "rgba(236, 72, 153, 0.15)" }, // pink
  { border: "rgba(20, 184, 166, 1)", background: "rgba(20, 184, 166, 0.15)" }, // teal
  { border: "rgba(234, 179, 8, 1)", background: "rgba(234, 179, 8, 0.15)" }, // yellow
  { border: "rgba(99, 102, 241, 1)", background: "rgba(99, 102, 241, 0.15)" }, // indigo
  { border: "rgba(239, 68, 68, 1)", background: "rgba(239, 68, 68, 0.15)" }, // red
  { border: "rgba(107, 114, 128, 1)", background: "rgba(107, 114, 128, 0.15)" }, // gray
];

export const SERIES_COLOR_COUNT = SERIES_COLORS.length;

export function getSeriesColor(index: number): {
  border: string;
  background: string;
} {
  return SERIES_COLORS[index % SERIES_COLORS.length];
}
