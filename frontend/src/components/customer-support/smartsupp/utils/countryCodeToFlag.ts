export function countryCodeToFlag(code: string): string {
  if (!code || code.length < 2) return "";
  const upper = code.toUpperCase().slice(0, 2);
  return Array.from(upper)
    .map((c) => String.fromCodePoint(0x1f1e6 + c.charCodeAt(0) - 65))
    .join("");
}
