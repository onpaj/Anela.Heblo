export function getPageWindow(totalPages: number, currentPage: number): number[] {
  const count = Math.min(Math.max(totalPages, 0), 5);
  return Array.from({ length: count }, (_, i) => {
    if (totalPages <= 5) return i + 1;
    if (currentPage <= 3) return i + 1;
    if (currentPage >= totalPages - 2) return totalPages - 4 + i;
    return currentPage - 2 + i;
  });
}
