import React from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ProductStatisticsTable from "../ProductStatisticsTable";

const months = ["2025-01", "2025-02", "2025-03"];
const series = [
  { productCode: "PROD-A", productName: "Krém", values: [120, 98, 143] },
  { productCode: "PROD-B", productName: "Mýdlo", values: [45, 51, 40] },
];

describe("ProductStatisticsTable", () => {
  test("renders a tab per product with the first one selected", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const tabs = screen.getAllByRole("tab");
    expect(tabs.map((tab) => tab.textContent)).toEqual([
      "Krém (PROD-A)",
      "Mýdlo (PROD-B)",
    ]);
    expect(tabs[0]).toHaveAttribute("aria-selected", "true");
    expect(tabs[1]).toHaveAttribute("aria-selected", "false");
  });

  test("renders only the selected product's table", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    expect(screen.getAllByRole("table")).toHaveLength(1);

    const headers = screen.getAllByRole("columnheader");
    expect(headers.map((h) => h.textContent)).toEqual(["Měsíc", "Množství"]);

    const marchRow = screen.getByRole("row", { name: /2025-03/ });
    expect(within(marchRow).getAllByRole("cell")[1]).toHaveTextContent("143");
  });

  test("switches the table when another product tab is clicked", async () => {
    const user = userEvent.setup();
    render(<ProductStatisticsTable months={months} series={series} />);

    await user.click(screen.getByRole("tab", { name: "Mýdlo (PROD-B)" }));

    const marchRow = screen.getByRole("row", { name: /2025-03/ });
    expect(within(marchRow).getAllByRole("cell")[1]).toHaveTextContent("40");
  });

  test("falls back to the first tab when the selected product is removed", async () => {
    const user = userEvent.setup();
    const { rerender } = render(
      <ProductStatisticsTable months={months} series={series} />,
    );

    await user.click(screen.getByRole("tab", { name: "Mýdlo (PROD-B)" }));
    rerender(<ProductStatisticsTable months={months} series={[series[0]]} />);

    const marchRow = screen.getByRole("row", { name: /2025-03/ });
    expect(within(marchRow).getAllByRole("cell")[1]).toHaveTextContent("143");
  });

  test("renders months newest first", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    const bodyRows = screen.getAllByRole("row").slice(1, 4);
    const firstCells = bodyRows.map(
      (row) => within(row).getAllByRole("cell")[0].textContent,
    );

    expect(firstCells).toEqual(["2025-03", "2025-02", "2025-01"]);
  });

  test("totals the selected product in the footer", () => {
    render(<ProductStatisticsTable months={months} series={series} />);

    // The totals row is always last; the header row also contains "Celkem"-like
    // text, so index rather than name-match it.
    const rows = screen.getAllByRole("row");
    const footerCells = within(rows[rows.length - 1]).getAllByRole("cell");

    expect(footerCells[1]).toHaveTextContent("361");
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
