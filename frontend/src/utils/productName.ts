/** Separator between a product name and its name suffix (ABRA Flexi "Popis FR") in single-line labels. */
export const PRODUCT_NAME_SUFFIX_SEPARATOR = " · ";

/** A suffix that is missing or whitespace-only counts as no suffix. */
export const normalizeProductNameSuffix = (
  suffix?: string | null,
): string | undefined => {
  const trimmed = suffix?.trim();
  return trimmed ? trimmed : undefined;
};

/** Single-line product name: `Name · Suffix`, or just `Name` when there is no suffix. */
export const formatProductNameWithSuffix = (
  name: string,
  suffix?: string | null,
): string => {
  const normalizedSuffix = normalizeProductNameSuffix(suffix);
  if (!normalizedSuffix) {
    return name;
  }
  return name
    ? `${name}${PRODUCT_NAME_SUFFIX_SEPARATOR}${normalizedSuffix}`
    : normalizedSuffix;
};

/** Single-line product label: `Name · Suffix (CODE)`, or `Name (CODE)` when there is no suffix. */
export const formatProductLabel = (
  name: string,
  code: string,
  suffix?: string | null,
): string => `${formatProductNameWithSuffix(name, suffix)} (${code})`;
