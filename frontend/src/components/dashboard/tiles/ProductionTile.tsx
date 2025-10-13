import React from 'react';
import { PackageCheck, Check } from 'lucide-react';

interface ProductionTileProps {
  data: {
    totalOrders?: number;
    products?: Array<{
      productName: string;
      semiProductCompleted: boolean;
      productsCompleted: boolean;
      responsiblePerson: string;
      actualQuantity: number;
    }>;
  };
  title: string;
}

export const ProductionTile: React.FC<ProductionTileProps> = ({ data, title }) => {
  const { totalOrders = 0, products = [] } = data;

  // Empty state when no production is scheduled
  if (totalOrders === 0 || products.length === 0) {
    return (
      <div className="h-full flex flex-col items-center justify-center text-center">
        <PackageCheck className="h-12 w-12 text-gray-300 mb-3" />
        <p className="text-sm font-medium text-gray-500 mb-1">
          Žádná výroba
        </p>
        <p className="text-xs text-gray-400">
          Pro {title.toLowerCase()} není naplánována žádná výroba
        </p>
      </div>
    );
  }

  return (
    <div className="h-full">
      <div className="space-y-3 max-h-24 overflow-y-auto">
        {products.slice(0, 3).map((product, index) => (
          <div key={index} className="flex justify-between items-center gap-2">
            <span className="text-base text-gray-900 truncate font-medium flex-1">
              {product.productName} - {product.actualQuantity}g{product.responsiblePerson ? ` (${product.responsiblePerson})` : ''}
            </span>
            <div className="flex items-center gap-1 flex-shrink-0">
              <div title={product.semiProductCompleted ? 'Polotovar hotový' : 'Polotovar čeká'}>
                <Check
                  className={`h-4 w-4 ${
                    product.semiProductCompleted ? 'text-green-500' : 'text-gray-300'
                  }`}
                />
              </div>
              <div title={product.productsCompleted ? 'Kompletní' : 'Nekompletní'}>
                <Check
                  className={`h-4 w-4 ${
                    product.productsCompleted ? 'text-green-500' : 'text-gray-300'
                  }`}
                />
              </div>
            </div>
          </div>
        ))}
        {products.length > 3 && (
          <div className="text-xs text-gray-500 pt-1">
            +{products.length - 3} další
          </div>
        )}
      </div>
    </div>
  );
};
