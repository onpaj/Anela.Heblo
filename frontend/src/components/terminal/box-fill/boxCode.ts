// A valid transport box code is the letter B followed by exactly 3 digits (e.g. B001).
export const isValidBoxCode = (code: string): boolean => /^B\d{3}$/i.test(code.trim());
