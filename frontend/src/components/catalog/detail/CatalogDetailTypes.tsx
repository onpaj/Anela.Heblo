import { ProductType } from '../../../api/hooks/useCatalog';

// Product type labels for display
export const productTypeLabels: Record<ProductType, string> = {
  [ProductType.Product]: 'Produkt',
  [ProductType.Goods]: 'Zboží',
  [ProductType.Material]: 'Materiál',
  [ProductType.SemiProduct]: 'Polotovar',
  [ProductType.UNDEFINED]: 'Nedefinováno',
};

// Product type colors for badges
export const productTypeColors: Record<ProductType, string> = {
  [ProductType.Product]: 'bg-blue-100 text-blue-800',
  [ProductType.Goods]: 'bg-green-100 text-green-800',
  [ProductType.Material]: 'bg-orange-100 text-orange-800',
  [ProductType.SemiProduct]: 'bg-purple-100 text-purple-800',
  [ProductType.UNDEFINED]: 'bg-gray-100 text-gray-800',
};

// Chart tab helper functions
export const getInputTabName = (productType: ProductType): string => {
  switch (productType) {
    case ProductType.Material:
    case ProductType.Goods:
      return 'Nákup';
    case ProductType.Product:
      return 'Výroba';
    case ProductType.SemiProduct:
      return 'Výroba';
    default:
      return '';
  }
};

export const getOutputTabName = (productType: ProductType): string => {
  switch (productType) {
    case ProductType.Material:
      return 'Spotřeba';
    case ProductType.Product:
    case ProductType.Goods:
      return 'Prodeje';
    case ProductType.SemiProduct:
      return 'Spotřeba';
    default:
      return '';
  }
};

export const shouldShowChartTabs = (productType: ProductType): boolean => {
  return productType !== ProductType.UNDEFINED;
};