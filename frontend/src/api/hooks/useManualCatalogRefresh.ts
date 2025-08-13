import { useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface RefreshOperation {
  key: string;
  name: string;
  methodName: string;
  description: string;
}

export const refreshOperations: RefreshOperation[] = [
  {
    key: 'transport',
    name: 'Transport Data',
    methodName: 'catalog_RefreshTransportData',
    description: 'Obnovit data přepravy a balení'
  },
  {
    key: 'reserve',
    name: 'Reserve Data',
    methodName: 'catalog_RefreshReserveData',
    description: 'Obnovit rezervační data'
  },
  {
    key: 'sales',
    name: 'Sales Data',
    methodName: 'catalog_RefreshSalesData',
    description: 'Obnovit data prodeje'
  },
  {
    key: 'attributes',
    name: 'Attributes Data',
    methodName: 'catalog_RefreshAttributesData',
    description: 'Obnovit atributová data'
  },
  {
    key: 'erp-stock',
    name: 'ERP Stock Data',
    methodName: 'catalog_RefreshErpStockData',
    description: 'Obnovit skladové zásoby z ERP'
  },
  {
    key: 'eshop-stock',
    name: 'E-shop Stock Data',
    methodName: 'catalog_RefreshEshopStockData',
    description: 'Obnovit skladové zásoby z e-shopu'
  },
  {
    key: 'purchase-history',
    name: 'Purchase History',
    methodName: 'catalog_RefreshPurchaseHistoryData',
    description: 'Obnovit historii nákupů'
  },
  {
    key: 'consumed-history',
    name: 'Consumed History',
    methodName: 'catalog_RefreshConsumedHistoryData',
    description: 'Obnovit historii spotřeby'
  },
  {
    key: 'stock-taking',
    name: 'Stock Taking',
    methodName: 'catalog_RefreshStockTakingData',
    description: 'Obnovit data inventury'
  },
  {
    key: 'lots',
    name: 'Lots Data',
    methodName: 'catalog_RefreshLotsData',
    description: 'Obnovit data šarží'
  },
  {
    key: 'eshop-prices',
    name: 'E-shop Prices',
    methodName: 'catalog_RefreshEshopPricesData',
    description: 'Obnovit ceny z e-shopu'
  },
  {
    key: 'erp-prices',
    name: 'ERP Prices',
    methodName: 'catalog_RefreshErpPricesData',
    description: 'Obnovit ceny z ERP'
  }
];

export const useManualCatalogRefresh = () => {
  return useMutation({
    mutationFn: async (methodName: string) => {
      const apiClient = await getAuthenticatedApiClient();
      
      // Call the generated method dynamically
      const method = (apiClient as any)[methodName];
      if (typeof method !== 'function') {
        throw new Error(`Method ${methodName} not found in API client`);
      }
      
      try {
        const result = await method.call(apiClient);
        return result;
      } catch (error: any) {
        throw new Error(`Failed to refresh catalog data: ${error.message}`);
      }
    }
  });
};