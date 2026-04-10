import ExcelJS from "exceljs";

export async function exportToXlsx<T>(
  rows: T[],
  columns: { header: string; value: (row: T) => unknown }[],
  filename: string,
): Promise<void> {
  const workbook = new ExcelJS.Workbook();
  const worksheet = workbook.addWorksheet("Sheet1");

  worksheet.addRow(columns.map((col) => col.header));
  rows.forEach((row) => {
    worksheet.addRow(columns.map((col) => col.value(row)));
  });

  const buffer = await workbook.xlsx.writeBuffer();
  const blob = new Blob([buffer], {
    type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
