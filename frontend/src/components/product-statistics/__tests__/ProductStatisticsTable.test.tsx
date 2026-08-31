import React from "react";
import { render, screen, within } from "@testing-library/react";
import ProductStatisticsTable from "../ProductStatisticsTable";

const months = ["2025-01", "2025-02", "2025-03"];
const series = [
  { productCode: "PROD-A", productName: "Krém", values: [120, 98, 143] },
  { productCode: "PROD-B", productName: "Mýdlo", values: [45, 51, 40] },
];

describe("ProductStatisticsTable", () => {
  test("renders a column per product plus a total column", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const headers = screen.getAllByRole("columnheader");
    expect(headers.map((h) => h.textContent)).toEqual([
      "Měsíc",
      "Krém (PROD-A)",
      "Mýdlo (PROD-B)",
      "Celkem",
    ]);
  });

  test("renders months newest first", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const bodyRows = screen.getAllByRole("row").slice(1, 4);
    const firstCells = bodyRows.map(
      (row) => within(row).getAllByRole("cell")[0].textContent,
    );

    expect(firstCells).toEqual(["2025-03", "2025-02", "2025-01"]);
  });

  test("totals each row across products", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const marchRow = screen.getByRole("row", { name: /2025-03/ });
    const cells = within(marchRow).getAllByRole("cell");

    expect(cells[cells.length - 1]).toHaveTextContent("183");
  });

  test("totals each product column in the footer", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    // Take the last row rather than getByRole("row", { name: /Celkem/ }): the header
    // row also contains "Celkem" (its last column header) and role-name matching is
    // substring-based, so the name query matches both. The totals row is always last.
    const rows = screen.getAllByRole("row");
    const footerRow = rows[rows.length - 1];
    const cells = within(footerRow).getAllByRole("cell");

    expect(cells[1]).toHaveTextContent("361");
    expect(cells[2]).toHaveTextContent("136");
    expect(cells[3]).toHaveTextContent("497");
  });

  test("renders months with no data as zero", () => {
    render(
      <ProductStatisticsTable
        months={["2025-01", "2025-02"]}
        series={[{ productCode: "PROD-A", productName: "Krém", values: [0, 7] }]}
      />,
    );

    const januaryRow = screen.getByRole("row", { name: /2025-01/ });
    expect(within(januaryRow).getAllByRole("cell")[1]).toHaveTextContent("0");
  });

  test("renders nothing but a hint when no products are selected", () => {
    render(<ProductStatisticsTable months={[]} series={[]} />);

    expect(screen.getByText("Žádná data k zobrazení")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
