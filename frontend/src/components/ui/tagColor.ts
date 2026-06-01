export interface TagColorPair {
  bg: string;
  text: string;
}

export const TAG_PALETTE: readonly TagColorPair[] = [
  { bg: "bg-blue-100", text: "text-blue-800" },
  { bg: "bg-emerald-100", text: "text-emerald-800" },
  { bg: "bg-rose-100", text: "text-rose-800" },
  { bg: "bg-amber-100", text: "text-amber-800" },
  { bg: "bg-violet-100", text: "text-violet-800" },
  { bg: "bg-pink-100", text: "text-pink-800" },
  { bg: "bg-cyan-100", text: "text-cyan-800" },
  { bg: "bg-lime-100", text: "text-lime-800" },
  { bg: "bg-orange-100", text: "text-orange-800" },
  { bg: "bg-slate-100", text: "text-slate-800" },
];

export const OVERLAY_PALETTE: readonly TagColorPair[] = [
  { bg: "bg-blue-500/90", text: "text-white" },
  { bg: "bg-emerald-500/90", text: "text-white" },
  { bg: "bg-rose-500/90", text: "text-white" },
  { bg: "bg-amber-500/90", text: "text-white" },
  { bg: "bg-violet-500/90", text: "text-white" },
  { bg: "bg-pink-500/90", text: "text-white" },
  { bg: "bg-cyan-500/90", text: "text-white" },
  { bg: "bg-lime-600/90", text: "text-white" },
  { bg: "bg-orange-500/90", text: "text-white" },
  { bg: "bg-slate-500/90", text: "text-white" },
];

export function getTagColor(
  name: string,
  overlay: boolean = false
): TagColorPair {
  const palette = overlay ? OVERLAY_PALETTE : TAG_PALETTE;
  const lowercased = name.toLowerCase();

  // djb2 hash: start at 5381, then hash = (hash * 33) ^ char for each character
  let hash = 5381;
  for (let i = 0; i < lowercased.length; i++) {
    hash = ((hash << 5) + hash) ^ lowercased.charCodeAt(i); // (hash * 33) ^ char
    hash = hash >>> 0; // keep as unsigned 32-bit integer
  }

  return palette[hash % palette.length];
}
