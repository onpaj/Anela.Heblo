import * as XLSX from "xlsx";

export function exportToXlsx<T>(
  rows: T[],
  columns: { header: string; value: (row: T) => unknown }[],
  filename: string,
): void {
  const headers = columns.map((col) => col.header);
  const dataRows = rows.map((row) => columns.map((col) => col.value(row)));

  const wsData = [headers, ...dataRows];
  const ws = XLSX.utils.aoa_to_sheet(wsData);
  const wb = XLSX.utils.book_new();
  XLSX.utils.book_append_sheet(wb, ws, "Sheet1");
  XLSX.writeFile(wb, filename);
}
