import React from "react";
import { DollarSign, Info } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";

interface ProductPriceInfoProps {
  item: CatalogItemDto;
}

const PURCHASE_VS_STOCK_PRICE_TOOLTIP =
  "Nákupní cena = kolik by stála příští výroba či nákup za dnešní ceny (ceník). " +
  "Skladová cena = skutečný vážený průměr cen kusů, které jsou právě na skladě. " +
  "Liší se, dokud se nespotřebují starší šarže nakoupené za jiné ceny.";

// Per-gram materials cost fractions of a crown (e.g. 0,3119 Kč/g), so below 1 Kč
// two decimals would round away most of the information.
const SUB_CROWN_FRACTION_DIGITS = 4;
const DEFAULT_FRACTION_DIGITS = 2;

const formatUnitPrice = (value?: number): string =>
  value
    ? `${value.toLocaleString("cs-CZ", {
        minimumFractionDigits: 0,
        maximumFractionDigits:
          Math.abs(value) < 1
            ? SUB_CROWN_FRACTION_DIGITS
            : DEFAULT_FRACTION_DIGITS,
      })} Kč`
    : "-";

const ProductPriceInfo: React.FC<ProductPriceInfoProps> = ({ item }) => {
  return (
    <div className="space-y-3">
      <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text flex items-center">
        <DollarSign className="h-5 w-5 mr-2 text-gray-500 dark:text-graphite-muted" />
        Cenové informace
      </h3>

      <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-3">
        {/* Check if we have any price data */}
        {item.price?.eshopPrice ||
        item.price?.erpPrice ||
        item.price?.stockPrice ? (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-white dark:bg-graphite-surface">
                <tr className="border-b border-gray-200 dark:border-graphite-border">
                  <th className="text-left py-2 pr-4 font-medium text-gray-700 dark:text-graphite-muted"></th>
                  <th className="text-center py-2 px-2 font-medium text-gray-700 dark:text-graphite-muted">
                    Shoptet
                  </th>
                  <th className="text-center py-2 pl-2 font-medium text-gray-700 dark:text-graphite-muted">
                    ABRA
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100 dark:divide-graphite-border">
                {/* Selling price with VAT row */}
                <tr>
                  <td className="py-2 pr-4 font-medium text-gray-600 dark:text-graphite-muted">
                    Prodejní s DPH:
                  </td>
                  <td className="text-center py-2 px-2">
                    {item.price?.eshopPrice?.priceWithVat
                      ? `${item.price.eshopPrice.priceWithVat.toLocaleString("cs-CZ", { minimumFractionDigits: 0 })} Kč`
                      : "-"}
                  </td>
                  <td className="text-center py-2 pl-2">
                    {item.price?.erpPrice?.priceWithVat
                      ? `${item.price.erpPrice.priceWithVat.toLocaleString("cs-CZ", { minimumFractionDigits: 0 })} Kč`
                      : "-"}
                  </td>
                </tr>

                {/* Selling price without VAT row */}
                <tr>
                  <td className="py-2 pr-4 font-medium text-gray-600 dark:text-graphite-muted">
                    Prodejní bez DPH:
                  </td>
                  <td className="text-center py-2 px-2">-</td>
                  <td className="text-center py-2 pl-2">
                    {item.price?.erpPrice?.priceWithoutVat
                      ? `${item.price.erpPrice.priceWithoutVat.toLocaleString("cs-CZ", { minimumFractionDigits: 0 })} Kč`
                      : "-"}
                  </td>
                </tr>

                {/* Purchase price row - what the next manufacture/purchase would cost */}
                <tr>
                  <td className="py-2 pr-4 font-medium text-gray-600 dark:text-graphite-muted">
                    <span className="inline-flex items-center gap-1">
                      Nákupní (příští výroba):
                      <span title={PURCHASE_VS_STOCK_PRICE_TOOLTIP}>
                        <Info
                          className="h-3.5 w-3.5 text-gray-400 dark:text-graphite-muted cursor-help"
                          aria-hidden="true"
                        />
                      </span>
                    </span>
                  </td>
                  <td className="text-center py-2 px-2">
                    {formatUnitPrice(item.price?.eshopPrice?.purchasePrice)}
                  </td>
                  <td className="text-center py-2 pl-2">
                    {formatUnitPrice(item.price?.erpPrice?.purchasePrice)}
                  </td>
                </tr>

                {/* Stock price row - what the pieces currently in stock actually cost */}
                <tr>
                  <td className="py-2 pr-4 font-medium text-gray-600 dark:text-graphite-muted">
                    Skladová (skutečná):
                  </td>
                  <td className="text-center py-2 px-2">-</td>
                  <td className="text-center py-2 pl-2">
                    {formatUnitPrice(item.price?.stockPrice)}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        ) : (
          <div className="text-center text-gray-500 dark:text-graphite-muted py-4">
            <span className="text-sm">Cenové informace nejsou k dispozici</span>
          </div>
        )}
      </div>
    </div>
  );
};

export default ProductPriceInfo;
