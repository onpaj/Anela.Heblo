import React from "react";
import { render, screen, within } from "@testing-library/react";
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

  it("rounds prices of 1 Kč and more to two decimals", () => {
    render(
      <ProductPriceInfo
        item={buildItem({
          stockPrice: 62.3456,
          erpPrice: { purchasePrice: 12.345 },
        })}
      />,
    );

    expect(purchasePriceRow()).toHaveTextContent("12,35 Kč");
    expect(stockPriceRow()).toHaveTextContent("62,35 Kč");
  });

  it("shows a dash when the item is not in stock", () => {
    render(
      <ProductPriceInfo
        item={buildItem({ erpPrice: { purchasePrice: 41.93 } })}
      />,
    );

    const [, shoptetCell, abraCell] =
      within(stockPriceRow()).getAllByRole("cell");
    expect(abraCell).toHaveTextContent(/^-$/);
    expect(shoptetCell).toHaveTextContent(/^-$/);
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
  describe("e-shop action price", () => {
    const sellingPriceRow = () =>
      screen.getByRole("row", { name: /Prodejní s DPH/ });

    it("shows the running action price with the regular price struck through", () => {
      render(
        <ProductPriceInfo
          item={buildItem({
            eshopPrice: {
              priceWithVat: 490,
              regularPriceWithVat: 539,
              actionPriceWithVat: 490,
              isInAction: true,
            },
          })}
        />,
      );

      const row = sellingPriceRow();
      expect(within(row).getByText("490 Kč")).toBeInTheDocument();
      expect(within(row).getByText("539 Kč").tagName).toBe("S");
      expect(within(row).getByText("Akce")).toBeInTheDocument();
      expect(screen.getByRole("row", { name: /Akce platí/ })).toHaveTextContent(
        "bez omezení",
      );
    });

    it("shows the action window when it has dates", () => {
      render(
        <ProductPriceInfo
          item={buildItem({
            eshopPrice: {
              priceWithVat: 490,
              regularPriceWithVat: 539,
              actionPriceWithVat: 490,
              actionFrom: "2026-09-01",
              actionUntil: "2026-10-31",
              isInAction: true,
            },
          })}
        />,
      );

      expect(screen.getByRole("row", { name: /Akce platí/ })).toHaveTextContent(
        "01. 09. 2026 – 31. 10. 2026",
      );
    });

    it("ignores an expired action and shows only the regular price", () => {
      render(
        <ProductPriceInfo
          item={buildItem({
            eshopPrice: {
              priceWithVat: 669,
              regularPriceWithVat: 669,
              actionPriceWithVat: 620,
              actionFrom: "2025-11-18",
              actionUntil: "2025-12-23",
              isInAction: false,
            },
          })}
        />,
      );

      expect(sellingPriceRow()).toHaveTextContent("669 Kč");
      expect(within(sellingPriceRow()).queryByText("Akce")).toBeNull();
      expect(screen.queryByRole("row", { name: /Akce platí/ })).toBeNull();
    });
  });
});
