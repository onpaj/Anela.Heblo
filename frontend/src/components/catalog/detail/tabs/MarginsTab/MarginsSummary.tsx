import React from 'react';
import { BarChart3 } from 'lucide-react';
import { CatalogItemDto } from '../../../../../api/hooks/useCatalog';
import { ManufactureCostDto } from '../../../../../api/generated/api-client';

interface MarginsSummaryProps {
  item: CatalogItemDto | null;
  manufactureCostHistory: ManufactureCostDto[];
}

const MarginsSummary: React.FC<MarginsSummaryProps> = ({ item, manufactureCostHistory }) => {
  // Calculate average manufacturing costs for display
  const averageMaterialCost = manufactureCostHistory.length > 0 
    ? manufactureCostHistory.reduce((sum, record) => sum + (record.materialCost || 0), 0) / manufactureCostHistory.length 
    : 0;
  
  const averageHandlingCost = manufactureCostHistory.length > 0 
    ? manufactureCostHistory.reduce((sum, record) => sum + (record.handlingCost || 0), 0) / manufactureCostHistory.length 
    : 0;
    
  const averageTotalCost = manufactureCostHistory.length > 0 
    ? manufactureCostHistory.reduce((sum, record) => sum + (record.total || 0), 0) / manufactureCostHistory.length 
    : 0;

  // Use pre-calculated margin values from backend
  const sellingPrice = item?.price?.eshopPrice?.priceWithoutVat || 0;
  const margin = item?.marginPercentage || 0;
  const marginAmount = item?.marginAmount || 0;

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
      <h4 className="text-md font-medium text-gray-900 mb-3 flex items-center">
        <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
        Přehled nákladů a marže
      </h4>
      
      {/* Compact cost breakdown */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 mb-4">
        <div className="text-center p-2 bg-green-50 rounded border border-green-200">
          <div className="text-xs font-medium text-gray-600 mb-1">Materiál</div>
          <div className="text-lg font-bold text-green-900">
            {averageMaterialCost.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>
        
        <div className="text-center p-2 bg-blue-50 rounded border border-blue-200">
          <div className="text-xs font-medium text-gray-600 mb-1">Zpracování</div>
          <div className="text-lg font-bold text-blue-900">
            {averageHandlingCost.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>
        
        <div className="text-center p-2 bg-purple-50 rounded border border-purple-200">
          <div className="text-xs font-medium text-gray-600 mb-1">Celkem náklady</div>
          <div className="text-lg font-bold text-purple-900">
            {averageTotalCost.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>
        
        <div className="text-center p-2 bg-orange-50 rounded border border-orange-200">
          <div className="text-xs font-medium text-gray-600 mb-1">Prodej (bez DPH)</div>
          <div className="text-lg font-bold text-orange-900">
            {sellingPrice.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
          </div>
          <div className="text-xs text-gray-500">Kč/ks</div>
        </div>
      </div>
      
      {/* Margin summary */}
      <div className="grid grid-cols-2 gap-4">
        <div className="text-center p-3 bg-amber-50 rounded-lg border border-amber-200">
          <div className="text-sm font-medium text-gray-600 mb-1">
            Marže v %
          </div>
          <div className={`text-2xl font-bold ${margin >= 0 ? 'text-amber-900' : 'text-red-900'}`}>
            {margin.toLocaleString('cs-CZ', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%
          </div>
          <div className="text-xs text-gray-500 mt-1">
            {margin >= 0 ? 'zisk' : 'ztráta'}
          </div>
        </div>
        
        <div className="text-center p-3 bg-amber-50 rounded-lg border border-amber-200">
          <div className="text-sm font-medium text-gray-600 mb-1">
            Marže v Kč
          </div>
          <div className={`text-2xl font-bold ${marginAmount >= 0 ? 'text-amber-900' : 'text-red-900'}`}>
            {marginAmount.toLocaleString('cs-CZ', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} Kč
          </div>
          <div className="text-xs text-gray-500 mt-1">
            za kus
          </div>
        </div>
      </div>
      
      {manufactureCostHistory.length === 0 && (
        <div className="mt-3 text-center text-sm text-gray-500">
          Žádná data o nákladech za posledních 13 měsíců
        </div>
      )}
      
      {sellingPrice === 0 && (
        <div className="mt-2 text-center text-xs text-amber-600 bg-amber-50 p-2 rounded">
          Není dostupná prodejní cena - marže nelze vypočítat
        </div>
      )}
    </div>
  );
};

export default MarginsSummary;