import React from 'react';
import { useNavigate } from 'react-router-dom';
import { Truck } from 'lucide-react';
import { 
  createFilteredUrl,
  isTileClickable, 
  getTileTooltip, 
  TileDataWithDrillDown 
} from '../../../utils/urlUtils';

interface PurchaseOrdersInTransitTileProps {
  data: TileDataWithDrillDown & {
    status?: string;
    data?: {
      count?: number;
      totalAmount?: number;
      formattedAmount?: string;
    };
    error?: string;
  };
  tileCategory?: string;
  tileTitle?: string;
}

export const PurchaseOrdersInTransitTile: React.FC<PurchaseOrdersInTransitTileProps> = ({ data, tileCategory, tileTitle }) => {
  const navigate = useNavigate();
  
  const isClickable = isTileClickable(data);
  const tooltip = getTileTooltip(data);

  const handleClick = () => {
    if (isClickable && data.drillDown?.filters) {
      // Purchase orders in transit should navigate to purchase orders page with state filter
      const url = createFilteredUrl('/purchase/orders', data.drillDown.filters);
      navigate(url);
    }
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

  // Extract data
  const formattedAmount = data.data?.formattedAmount ?? '0k';
  const count = data.data?.count ?? 0;

  return (
    <div 
      className={`
        flex flex-col items-center justify-center h-full
        ${isClickable ? 'cursor-pointer hover:bg-gray-50 transition-colors duration-200 rounded-lg' : ''}
      `}
      onClick={handleClick}
      title={tooltip}
    >
      <div className="mb-2 text-orange-600">
        <Truck className="h-10 w-10" />
      </div>
      <div className="text-3xl font-bold text-gray-900 mb-1">
        {formattedAmount}
      </div>
      <div className="text-sm text-gray-500">
        {count} objednávek
      </div>
      {isClickable && (
        <div className="text-xs text-gray-500 mt-1 opacity-0 group-hover:opacity-100 transition-opacity">
          Klikněte pro detail
        </div>
      )}
    </div>
  );
};