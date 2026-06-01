import React from "react";
import { DollarSign } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";

interface ProductPriceInfoProps {
  item: CatalogItemDto;
}

const ProductPriceInfo: React.FC<ProductPriceInfoProps> = ({ item }) => {
  return (
    <div className="space-y-3">
      <h3 className="text-lg font-medium text-gray-900 flex items-center">
        <DollarSign className="h-5 w-5 mr-2 text-gray-500" />
        Cenové informace
      </h3>

      <div className="bg-gray-50 rounded-lg p-3">
        {/* Check if we have any price data */}
        {item.price?.eshopPrice || item.price?.erpPrice ? (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-10 bg-white">
                <tr className="border-b border-gray-200">
                  <th className="text-left py-2 pr-4 font-medium text-gray-700"></th>
                  <th className="text-center py-2 px-2 font-medium text-gray-700">
                    Shoptet
                  </th>
                  <th className="text-center py-2 pl-2 font-medium text-gray-700">
                    ABRA
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {/* Selling price with VAT row */}
                <tr>
                  <td className="py-2 pr-4 font-medium text-gray-600">
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
                  <td className="py-2 pr-4 font-medium text-gray-600">
                    Prodejní bez DPH:
                  </td>
                  <td className="text-center py-2 px-2">-</td>
                  <td className="text-center py-2 pl-2">
                    {item.price?.erpPrice?.priceWithoutVat
                      ? `${item.price.erpPrice.priceWithoutVat.toLocaleString("cs-CZ", { minimumFractionDigits: 0 })} Kč`
                      : "-"}
                  </td>
                </tr>

                {/* Purchase price row */}
                <tr>
                  <td className="py-2 pr-4 font-medium text-gray-600">
                    Nákupní:
                  </td>
                  <td className="text-center py-2 px-2">
                    {item.price?.eshopPrice?.purchasePrice
                      ? `${item.price.eshopPrice.purchasePrice.toLocaleString("cs-CZ", { minimumFractionDigits: 0 })} Kč`
                      : "-"}
                  </td>
                  <td className="text-center py-2 pl-2">
                    {item.price?.erpPrice?.purchasePrice
                      ? `${item.price.erpPrice.purchasePrice.toLocaleString("cs-CZ", { minimumFractionDigits: 0 })} Kč`
                      : "-"}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        ) : (
          <div className="text-center text-gray-500 py-4">
            <span className="text-sm">Cenové informace nejsou k dispozici</span>
          </div>
        )}
      </div>
    </div>
  );
};

export default ProductPriceInfo;
