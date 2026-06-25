import React from 'react';
import { useNavigate } from 'react-router-dom';
import { PackageCheck, Check } from 'lucide-react';
import { 
  createFilteredUrl,
  isTileClickable, 
  getTileTooltip, 
  TileDataWithDrillDown 
} from '../../../utils/urlUtils';

interface ProductionItem {
  productName: string;
  semiProductCompleted: boolean;
  productsCompleted: boolean;
  responsiblePerson: string;
  actualQuantity: number;
}

interface ProductionTileProps {
  data: TileDataWithDrillDown;
  title: string;
}

export const ProductionTile: React.FC<ProductionTileProps> = ({ data, title }) => {
  const navigate = useNavigate();
  const totalOrders = data.data?.totalOrders ?? 0;
  const products: ProductionItem[] = data.data?.products ?? [];
  
  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && data.drillDown?.filters) {
      // Production tiles should navigate to manufacturing orders page with date filter
      const url = createFilteredUrl('/manufacturing/orders', data.drillDown.filters);
      navigate(url);
    }
  };

  // Empty state when no production is scheduled
  if (totalOrders === 0 || products.length === 0) {
    return (
      <div
        className={`
          h-full flex flex-col items-center justify-center text-center min-h-44
          ${isClickable ? 'cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 active:bg-gray-100 dark:active:bg-white/10 transition-colors duration-200 rounded-lg' : ''}
        `}
        onClick={handleClick}
        title={tooltip}
        style={isClickable ? { touchAction: 'manipulation' } : undefined}
      >
        <PackageCheck className="h-12 w-12 text-gray-300 dark:text-graphite-faint mb-3" />
        <p className="text-sm font-medium text-gray-500 dark:text-graphite-muted mb-1">
          Žádná výroba
        </p>
        <p className="text-xs text-gray-400 dark:text-graphite-faint">
          Pro {title.toLowerCase()} není naplánována žádná výroba
        </p>
      </div>
    );
  }

  return (
    <div
      className={`
        h-full min-h-44
        ${isClickable ? 'cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 active:bg-gray-100 dark:active:bg-white/10 transition-colors duration-200 rounded-lg' : ''}
      `}
      onClick={handleClick}
      title={tooltip}
      style={isClickable ? { touchAction: 'manipulation' } : undefined}
    >
      <div className="space-y-3 h-48 md:h-auto max-h-72 overflow-y-auto leading-relaxed">
        {products.slice(0, 3).map((product, index) => (
          <div key={index} className="flex justify-between items-center gap-2">
            <span className="text-base text-gray-900 dark:text-graphite-text truncate font-medium flex-1">
              {product.productName} - {product.actualQuantity}g{product.responsiblePerson ? ` (${product.responsiblePerson})` : ''}
            </span>
            <div className="flex items-center gap-1 flex-shrink-0">
              <div title={product.semiProductCompleted ? 'Polotovar hotový' : 'Polotovar čeká'}>
                <Check
                  className={`h-5 w-5 md:h-4 md:w-4 ${
                    product.semiProductCompleted ? 'text-green-500 dark:text-emerald-400' : 'text-gray-300 dark:text-graphite-faint'
                  }`}
                />
              </div>
              <div title={product.productsCompleted ? 'Kompletní' : 'Nekompletní'}>
                <Check
                  className={`h-5 w-5 md:h-4 md:w-4 ${
                    product.productsCompleted ? 'text-green-500 dark:text-emerald-400' : 'text-gray-300 dark:text-graphite-faint'
                  }`}
                />
              </div>
            </div>
          </div>
        ))}
        {products.length > 3 && (
          <div className="text-sm md:text-xs text-gray-500 dark:text-graphite-muted pt-1">
            +{products.length - 3} další
          </div>
        )}
      </div>
    </div>
  );
};
