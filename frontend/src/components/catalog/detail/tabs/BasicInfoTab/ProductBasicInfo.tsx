import React from "react";
import { Hash, MapPin } from "lucide-react";
import {
  CatalogItemDto,
  ProductType,
} from "../../../../../api/hooks/useCatalog";
import { productTypeLabels, productTypeColors } from "../../CatalogDetailTypes";

interface ProductBasicInfoProps {
  item: CatalogItemDto;
}

const ProductBasicInfo: React.FC<ProductBasicInfoProps> = ({ item }) => {
  return (
    <div className="space-y-4">
      <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text flex items-center">
        <Hash className="h-5 w-5 mr-2 text-gray-500 dark:text-graphite-muted" />
        Základní informace
      </h3>

      <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4 space-y-3">
        <div className="flex justify-between items-center">
          <span className="text-sm font-medium text-gray-600 dark:text-graphite-muted">
            Typ produktu:
          </span>
          <span
            className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${productTypeColors[(item.type || ProductType.UNDEFINED) as ProductType]}`}
          >
            {
              productTypeLabels[
                (item.type || ProductType.UNDEFINED) as ProductType
              ]
            }
          </span>
        </div>

        <div className="flex justify-between items-center">
          <span className="text-sm font-medium text-gray-600 dark:text-graphite-muted flex items-center">
            <MapPin className="h-4 w-4 mr-1" />
            Umístění:
          </span>
          <span className="text-sm text-gray-900 dark:text-graphite-text">
            {item.location || "Není uvedeno"}
          </span>
        </div>

        <div className="flex justify-between items-center">
          <span className="text-sm font-medium text-gray-600 dark:text-graphite-muted">
            Min. objednávka:
          </span>
          <span className="text-sm text-gray-900 dark:text-graphite-text">
            {item.minimalOrderQuantity || "Není uvedeno"}
          </span>
        </div>

        <div className="flex justify-between items-center">
          <span className="text-sm font-medium text-gray-600 dark:text-graphite-muted">
            Min. výroba:
          </span>
          <span className="text-sm text-gray-900 dark:text-graphite-text">
            {item.minimalManufactureQuantity || "Není uvedeno"}
          </span>
        </div>

        <div className="flex justify-between items-center">
          <span className="text-sm font-medium text-gray-600 dark:text-graphite-muted">Dodavatel:</span>
          <span className="text-sm text-gray-900 dark:text-graphite-text">
            {item.supplierName || "Není uvedeno"}
          </span>
        </div>

        {/* Note field */}
        {item.note && (
          <div className="border-t border-gray-200 dark:border-graphite-border pt-3 mt-3">
            <div className="flex items-start">
              <span className="text-sm font-medium text-gray-600 dark:text-graphite-muted mr-3 flex-shrink-0">
                Poznámka:
              </span>
              <p className="text-sm text-gray-900 dark:text-graphite-text whitespace-pre-wrap flex-1">
                {item.note}
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default ProductBasicInfo;
