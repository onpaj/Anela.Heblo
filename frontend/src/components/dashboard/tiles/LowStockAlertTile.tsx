import React from 'react';
import { useNavigate } from 'react-router-dom';
import { 
  createFilteredUrl,
  isTileClickable, 
  getTileTooltip, 
  TileDataWithDrillDown 
} from '../../../utils/urlUtils';

interface LowStockProduct {
  productCode: string;
  productName: string;
  eshopStock: number;
  reserveStock: number;
  transportStock: number;
  averageDailySales: number;
  daysOfStockRemaining: number;
}

interface LowStockAlertTileProps {
  data: TileDataWithDrillDown & {
    status?: string;
    data?: {
      products?: LowStockProduct[];
      totalCount?: number;
    };
    error?: string;
    metadata?: {
      lastUpdated?: string;
      source?: string;
    };
  };
}

export const LowStockAlertTile: React.FC<LowStockAlertTileProps> = ({ data }) => {
  const navigate = useNavigate();
  
  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && data.drillDown?.filters) {
      const url = createFilteredUrl('/logistics/inventory', data.drillDown.filters);
      navigate(url);
    }
  };

  const handleRowClick = (product: LowStockProduct) => {
    // Navigate to inventory with filter for specific product
    const url = createFilteredUrl('/logistics/inventory', { 
      search: product.productCode,
      sort: 'eshop_stock_asc' 
    });
    navigate(url);
  };

  // Error state
  if (data.status === 'error') {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <div className="text-red-500 text-2xl mb-2">⚠️</div>
          <p className="text-red-600 text-sm">{data.error || 'Chyba při načítání dat'}</p>
        </div>
      </div>
    );
  }

  const products = data.data?.products || [];
  const totalCount = data.data?.totalCount || 0;

  // No low stock products
  if (products.length === 0) {
    return (
      <div className="h-full flex items-center justify-center text-center">
        <div>
          <div className="text-green-500 text-2xl mb-2">✅</div>
          <p className="text-green-600 text-sm font-medium">Všechny produkty mají dostatečnou zásobu</p>
        </div>
      </div>
    );
  }

  // Format number with Czech locale
  const formatNumber = (num: number, decimals: number = 0): string => {
    return new Intl.NumberFormat('cs-CZ', { 
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals 
    }).format(num);
  };

  // Format days remaining with special handling for very high values
  const formatDaysRemaining = (days: number): string => {
    if (days === Number.MAX_VALUE || days > 9999) {
      return '∞';
    }
    return formatNumber(days);
  };

  return (
    <div className="h-full flex flex-col text-xs">
      {/* Compact products list */}
      <div className="flex-1 overflow-auto">
        <div className="space-y-1">
          {products.slice(0, 5).map((product) => (
            <div
              key={product.productCode}
              className="p-2 hover:bg-gray-50 cursor-pointer transition-colors duration-200 rounded border border-gray-100"
              onClick={() => handleRowClick(product)}
            >
              {/* Two line product row */}
              <div>
                <div className="flex items-center justify-between text-xs">
                  <div className="font-medium text-gray-900 truncate">
                    {product.productName}
                  </div>
                  <div className="font-mono text-xs text-gray-600">
                    {formatNumber(product.eshopStock)} / {formatNumber(product.reserveStock)} / {formatNumber(product.transportStock)}
                  </div>
                </div>
                <div className="text-xs text-gray-500">
                  {product.productCode}
                </div>
              </div>
            </div>
          ))}
          {totalCount > 5 && (
            <div className="text-center pt-1">
              <button
                onClick={handleClick}
                className="text-xs text-blue-600 hover:text-blue-800 hover:underline"
                title={tooltip}
              >
                +{totalCount - 5} dalších
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};