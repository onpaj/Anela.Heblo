import React from "react";
import { render, screen } from "@testing-library/react";
import { CatalogItemDto } from "../../../../../../api/hooks/useCatalog";
import ProductPriceInfo from "../ProductPriceInfo";

const buildItem = (price: Record<string, unknown>) =>
  ({ productCode: "DEZ001100", price }) as unknown as CatalogItemDto;

const stockPriceRow = () =>
  screen.getByRole("row", { name: /Skladová \(skutečná\)/ });

const purchasePriceRow = () =>
  screen.getByRole("row", { name: /Nákupní \(příští výroba\)/ });

describe("ProductPriceInfo", () => {
  it("shows the stock price next to the purchase price", () => {
    render(
      <ProductPriceInfo
        item={buildItem({
          stockPrice: 62.35,
          erpPrice: {
            purchasePrice: 41.93,
            priceWithVat: 0,
            priceWithoutVat: 0,
          },
        })}
      />,
    );

    expect(purchasePriceRow()).toHaveTextContent("41,93 Kč");
    expect(stockPriceRow()).toHaveTextContent("62,35 Kč");
  });

  it("explains the difference between the two prices in a tooltip", () => {
    render(
      <ProductPriceInfo
        item={buildItem({
          stockPrice: 62.35,
          erpPrice: { purchasePrice: 41.93 },
        })}
      />,
    );

    const tooltip = screen.getByTitle(/skutečný vážený průměr/);
    expect(tooltip).toBeInTheDocument();
    expect(tooltip.getAttribute("title")).toMatch(/dnešní ceny/);
  });

  it("keeps enough precision for per-gram materials", () => {
    render(
      <ProductPriceInfo
        item={buildItem({
          stockPrice: 0.311901,
          erpPrice: { purchasePrice: 0.52 },
        })}
      />,
    );

    expect(stockPriceRow()).toHaveTextContent("0,3119 Kč");
    expect(purchasePriceRow()).toHaveTextContent("0,52 Kč");
  });

  it("shows a dash when the item is not in stock", () => {
    render(
      <ProductPriceInfo
        item={buildItem({ erpPrice: { purchasePrice: 41.93 } })}
      />,
    );

    expect(stockPriceRow()).toHaveTextContent("-");
    expect(stockPriceRow()).not.toHaveTextContent("Kč");
  });

  it("renders the price table when only the stock price is known", () => {
    render(<ProductPriceInfo item={buildItem({ stockPrice: 17.67 })} />);

    expect(stockPriceRow()).toHaveTextContent("17,67 Kč");
    expect(
      screen.queryByText("Cenové informace nejsou k dispozici"),
    ).not.toBeInTheDocument();
  });

  it("shows the empty state when no price is known", () => {
    render(<ProductPriceInfo item={buildItem({})} />);

    expect(
      screen.getByText("Cenové informace nejsou k dispozici"),
    ).toBeInTheDocument();
  });
});
