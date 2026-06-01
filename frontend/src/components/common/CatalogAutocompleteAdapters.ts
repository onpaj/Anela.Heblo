import { CatalogItemDto, ProductType } from "../../api/generated/api-client";
import { MaterialForPurchaseDto } from "../../api/hooks/useMaterials";

/**
 * Adapter function to convert CatalogItemDto to MaterialForPurchaseDto
 * Used for purchase orders and material selection
 */
export const catalogItemToMaterial = (
  item: CatalogItemDto,
): MaterialForPurchaseDto => ({
  productCode: item.productCode,
  productName: item.productName,
  productType: item.type?.toString() || "Material",
  location: item.location,
  currentStock: item.stock?.available,
  minimalOrderQuantity: item.minimalOrderQuantity,
  lastPurchasePrice: item.price?.currentPurchasePrice
    ? Number(item.price.currentPurchasePrice)
    : undefined,
});

/**
 * Simple product code adapter for cases where only product code is needed
 * Used for Journal entries and simple filters
 */
export const catalogItemToProductCode = (item: CatalogItemDto): string => {
  return item.productCode || "";
};

/**
 * Simple display adapter - returns just the product name
 */
export const catalogItemToProductName = (item: CatalogItemDto): string => {
  return item.productName || item.productCode || "";
};

/**
 * Combined product code and name for display
 */
export const catalogItemToCodeAndName = (item: CatalogItemDto): string => {
  if (item.productCode && item.productName) {
    return `${item.productCode} - ${item.productName}`;
  }
  return item.productName || item.productCode || "";
};

/**
 * Display value functions for different data types
 */
export const materialDisplayValue = (
  material: MaterialForPurchaseDto,
): string => {
  return material.productName || material.productCode || "";
};

export const productCodeDisplayValue = (productCode: string): string => {
  return productCode;
};

export const catalogItemDisplayValue = (item: CatalogItemDto): string => {
  return item.productName || item.productCode || "";
};

/**
 * Common product type filters for different use cases
 */
export const PRODUCT_TYPE_FILTERS = {
  // For purchase orders - only materials and goods
  PURCHASE_MATERIALS: [
    ProductType.Material,
    ProductType.Goods,
  ] as ProductType[],

  // For manufacturing - materials and semi-products
  MANUFACTURING: [
    ProductType.Material,
    ProductType.SemiProduct,
  ] as ProductType[],

  // For transport/shipping - finished products
  FINISHED_PRODUCTS: [ProductType.Product, ProductType.Goods] as ProductType[],

  // All types (no filter)
  ALL: undefined,
};
