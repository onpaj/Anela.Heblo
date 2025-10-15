import React from 'react';
import { Truck } from 'lucide-react';

interface PurchaseOrdersInTransitTileProps {
  data: {
    status?: string;
    data?: {
      count?: number;
      totalAmount?: number;
      formattedAmount?: string;
    };
    error?: string;
  };
}

export const PurchaseOrdersInTransitTile: React.FC<PurchaseOrdersInTransitTileProps> = ({ data }) => {
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
    <div className="flex flex-col items-center justify-center h-full">
      <div className="mb-2 text-orange-600">
        <Truck className="h-10 w-10" />
      </div>
      <div className="text-3xl font-bold text-gray-900 mb-1">
        {formattedAmount}
      </div>
      <div className="text-sm text-gray-500">
        {count} objednávek
      </div>
    </div>
  );
};