import React from 'react';
import { BarChart3 } from 'lucide-react';
import { ProductType, CatalogSalesRecordDto, CatalogConsumedRecordDto, CatalogPurchaseRecordDto, CatalogManufactureRecordDto } from '../../../../api/hooks/useCatalog';

interface ProductSummaryTabsProps {
  productType: ProductType;
  activeTab: 'input' | 'output';
  salesData: CatalogSalesRecordDto[];
  consumedData: CatalogConsumedRecordDto[];
  purchaseData: CatalogPurchaseRecordDto[];
  manufactureData: CatalogManufactureRecordDto[];
}

const ProductSummaryTabs: React.FC<ProductSummaryTabsProps> = ({ 
  productType, 
  activeTab, 
  salesData, 
  consumedData, 
  purchaseData,
  manufactureData 
}) => {
  // Calculate summary statistics based on active tab and product type
  const calculateSummary = () => {
    const currentDate = new Date();
    const oneYearAgo = new Date(currentDate.getFullYear() - 1, currentDate.getMonth(), 1);
    
    let totalForLastYear = 0;
    let monthsWithData = 0;
    let dataLabel = '';
    let unitLabel = '';
    
    if (activeTab === 'input') {
      if (productType === ProductType.Material || productType === ProductType.Goods) {
        // Purchase data
        purchaseData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amount || 0;
              if ((record.amount || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'nákup';
        unitLabel = 'kusů';
      } else if (productType === ProductType.Product || productType === ProductType.SemiProduct) {
        // Manufacture data
        manufactureData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amount || 0;
              if ((record.amount || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'výroba';
        unitLabel = 'kusů';
      }
    } else {
      // Output tab
      if (productType === ProductType.Material || productType === ProductType.SemiProduct) {
        // Consumption data
        consumedData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amount || 0;
              if ((record.amount || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'spotřeba';
        unitLabel = 'množství';
      } else if (productType === ProductType.Product || productType === ProductType.Goods) {
        // Sales data
        salesData.forEach(record => {
          if (record.year && record.month) {
            const recordDate = new Date(record.year, record.month - 1);
            if (recordDate >= oneYearAgo) {
              totalForLastYear += record.amountTotal || 0;
              if ((record.amountTotal || 0) > 0) monthsWithData++;
            }
          }
        });
        dataLabel = 'prodeje';
        unitLabel = 'kusů';
      }
    }
    
    const averageMonthly = monthsWithData > 0 ? totalForLastYear / 12 : 0;
    
    return {
      totalForLastYear: Math.round(totalForLastYear * 100) / 100,
      averageMonthly: Math.round(averageMonthly * 100) / 100,
      monthsWithData,
      dataLabel,
      unitLabel
    };
  };

  const summary = calculateSummary();

  return (
    <div className="bg-white rounded-lg border border-gray-200 p-4 shadow-sm">
      <h4 className="text-md font-medium text-gray-900 mb-3 flex items-center">
        <BarChart3 className="h-4 w-4 mr-2 text-gray-500" />
        Celkové shrnutí - {summary.dataLabel}
      </h4>
      
      <div className="grid grid-cols-2 gap-4">
        <div className="text-center p-3 bg-blue-50 rounded-lg">
          <div className="text-sm font-medium text-gray-600 mb-1">
            Celkový {summary.dataLabel} za rok
          </div>
          <div className="text-2xl font-bold text-blue-900">
            {summary.totalForLastYear.toLocaleString('cs-CZ')}
          </div>
          <div className="text-xs text-gray-500 mt-1">
            za posledních 12 měsíců
          </div>
        </div>
        
        <div className="text-center p-3 bg-green-50 rounded-lg">
          <div className="text-sm font-medium text-gray-600 mb-1">
            Průměrný {summary.dataLabel}/měsíc
          </div>
          <div className="text-2xl font-bold text-green-900">
            {summary.averageMonthly.toLocaleString('cs-CZ')}
          </div>
          <div className="text-xs text-gray-500 mt-1">
            měsíční průměr
          </div>
        </div>
      </div>
      
      {summary.monthsWithData === 0 && (
        <div className="mt-3 text-center text-sm text-gray-500">
          Žádná data za posledních 12 měsíců
        </div>
      )}
    </div>
  );
};

export default ProductSummaryTabs;