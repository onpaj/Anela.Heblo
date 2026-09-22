/**
 * Czech counts take three forms: one (1), few (2-4) and many (0, 5 and up). Picking
 * between them is a grammatical rule rather than a tunable, which is why the
 * boundaries live here once instead of as bare numbers inside each message.
 */
export type CzechCountForm = "one" | "few" | "many";

const FEW_LOWER_BOUND = 2;
const FEW_UPPER_BOUND = 4;

export const czechCountForm = (count: number): CzechCountForm => {
  if (count === 1) {
    return "one";
  }
  if (count >= FEW_LOWER_BOUND && count <= FEW_UPPER_BOUND) {
    return "few";
  }
  return "many";
};

/** Picks the form matching `count` from the three a Czech noun phrase needs. */
export const czechPlural = (
  count: number,
  forms: Record<CzechCountForm, string>,
): string => forms[czechCountForm(count)];
