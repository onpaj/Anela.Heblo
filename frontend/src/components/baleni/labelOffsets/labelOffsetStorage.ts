/**
 * Per-computer shipping-label offsets. Each packing PC has its own printer quirks, so the offsets
 * are kept in this browser only and sent with every label request; the backend applies the one
 * for the order's carrier. X moves the label towards the PDF page's right edge, Y towards its top
 * edge, in whole millimetres (negative = left / down).
 */

export const LABEL_CARRIERS = ['Zasilkovna', 'PPL', 'GLS'] as const;
export type LabelCarrier = (typeof LABEL_CARRIERS)[number];

export const CARRIER_NAMES: Record<LabelCarrier, string> = {
  Zasilkovna: 'Zásilkovna',
  PPL: 'PPL',
  GLS: 'GLS',
};

/** Largest move allowed in either direction; mirrors LabelOffsetQuery.MaxOffsetMm on the backend. */
const MAX_LABEL_OFFSET_MM = 30;

export interface LabelOffset {
  xMm: number;
  yMm: number;
}

export type LabelOffsets = Partial<Record<LabelCarrier, LabelOffset>>;

export const ZERO_OFFSET: LabelOffset = { xMm: 0, yMm: 0 };

const STORAGE_KEY = 'heblo.baleni.labelOffsets';

const isValidMm = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value) && Math.abs(value) <= MAX_LABEL_OFFSET_MM;

export const clampMm = (value: number): number =>
  Math.max(-MAX_LABEL_OFFSET_MM, Math.min(MAX_LABEL_OFFSET_MM, Math.round(value)));

const isZeroOffset = (offset: LabelOffset): boolean => offset.xMm === 0 && offset.yMm === 0;

/** Reads this browser's offsets, dropping anything malformed. Never throws. */
export const loadLabelOffsets = (): LabelOffsets => {
  let raw: string | null;
  try {
    raw = window.localStorage.getItem(STORAGE_KEY);
  } catch {
    return {};
  }
  if (!raw) return {};

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return {};
  }
  if (!parsed || typeof parsed !== 'object') return {};

  return LABEL_CARRIERS.reduce<LabelOffsets>((acc, carrier) => {
    const offset = (parsed as Record<string, unknown>)[carrier] as Partial<LabelOffset> | undefined;
    return offset && isValidMm(offset.xMm) && isValidMm(offset.yMm)
      ? { ...acc, [carrier]: { xMm: offset.xMm, yMm: offset.yMm } }
      : acc;
  }, {});
};

/** Saves one carrier's offset in this browser. Returns false when the browser refuses to store it. */
export const saveLabelOffset = (carrier: LabelCarrier, offset: LabelOffset): boolean => {
  const next = { ...loadLabelOffsets(), [carrier]: { xMm: clampMm(offset.xMm), yMm: clampMm(offset.yMm) } };
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    return true;
  } catch {
    return false;
  }
};

/** Serialises offsets for the label endpoint: `Carrier:xMm:yMm`, comma-separated, zero offsets omitted. */
export const toOffsetsQuery = (offsets: LabelOffsets): string =>
  LABEL_CARRIERS.flatMap((carrier) => {
    const offset = offsets[carrier];
    return offset && !isZeroOffset(offset) ? [`${carrier}:${offset.xMm}:${offset.yMm}`] : [];
  }).join(',');
