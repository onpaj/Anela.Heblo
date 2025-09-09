import React from "react";
import { BarChart3 } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";

interface ProductStockInfoProps {
  item: CatalogItemDto;
}

const ProductStockInfo: React.FC<ProductStockInfoProps> = ({ item }) => {
  return (
    <div className="space-y-3">
      <h3 className="text-lg font-medium text-gray-900 flex items-center">
        <BarChart3 className="h-5 w-5 mr-2 text-gray-500" />
        Skladové zásoby
      </h3>

      <div className="bg-gray-50 rounded-lg p-3 space-y-2">
        {/* Available stock - highlighted */}
        <div className="flex justify-between items-center">
          <span className="text-sm font-medium text-gray-600">Dostupné:</span>
          <span className="inline-flex items-center px-2 py-1 rounded-full text-sm font-semibold bg-green-100 text-green-800">
            {Math.round((item.stock?.available || 0) * 100) / 100}
          </span>
        </div>

        {/* Other stock info in compact grid */}
        <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm border-t border-gray-200 pt-2">
          <div className="flex justify-between">
            <span className="text-gray-600">Shoptet:</span>
            <span className="font-medium">
              {Math.round((item.stock?.eshop || 0) * 100) / 100}
            </span>
          </div>

          <div className="flex justify-between">
            <span className="text-gray-600">Transport:</span>
            <span className="font-medium">
              {Math.round((item.stock?.transport || 0) * 100) / 100}
            </span>
          </div>

          <div className="flex justify-between">
            <span className="text-gray-600">ABRA:</span>
            <span className="font-medium">
              {Math.round((item.stock?.erp || 0) * 100) / 100}
            </span>
          </div>

          <div className="flex justify-between">
            <span className="text-gray-600">V rezerve:</span>
            <span className="font-medium">
              {Math.round((item.stock?.reserve || 0) * 100) / 100}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ProductStockInfo;
