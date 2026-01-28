/**
 * Test Data Fixtures
 *
 * This module provides well-known test data for E2E tests.
 * Data is based on the development/staging environment.
 *
 * Source: docs/testing/test-data-fixtures.md
 * Last Updated: 2026-01-25
 */

export interface CatalogItem {
  code: string;
  name: string;
  type: 'Materiál' | 'Polotovar' | 'Produkt' | 'Zboží' | 'Dárkový balíček';
  availableStock?: number;
  moq?: string;
  supplier?: string;
}

export interface PurchaseOrder {
  orderNumber: string;
  supplier: string;
  orderDate: string;
  status: 'Návrh' | 'V přepravě' | 'Dokončeno';
  hasInvoice: boolean;
  totalAmount: string;
  itemCount: number;
}

export interface ManufacturingOrder {
  orderNumber: string;
  status: 'Návrh' | 'Planned' | 'SemiProductManufactured' | 'Completed';
  productionDate: string;
  product: string;
  productCode: string;
  variantCount: number;
}

export interface TransportBox {
  code: string;
  state: 'Nový' | 'Otevřený' | 'V přepravě' | 'Přijatý' | 'Naskladněný' | 'V rezervě' | 'Uzavřený' | 'Chyba';
  itemCount: number;
  lastUpdate: string;
}

/**
 * Well-known catalog items for testing
 */
export const TestCatalogItems: Record<string, CatalogItem> = {
  // Stable material with good stock
  bisabolol: {
    code: 'AKL001',
    name: 'Bisabolol',
    type: 'Materiál',
    availableStock: 2762.39,
    moq: '1500g',
    supplier: 'Supplier MH' // Anonymized for security
  },

  // High-stock material
  dermosoftEco: {
    code: 'AKL003',
    name: 'Dermosoft Eco 1388',
    type: 'Materiál',
    availableStock: 47033.34,
    moq: '25000g'
  },

  // Semi-product with variants
  hedvabnyPan: {
    code: 'MAS001001M',
    name: 'Hedvábný pan Jasmín',
    type: 'Polotovar'
  },

  // Low-stock material
  hyacolor: {
    code: 'AKL009',
    name: 'Hyacolor TM',
    type: 'Materiál',
    availableStock: 500
  },

  // Common materials
  glycerol: {
    code: 'AKL007',
    name: 'Glycerol 99% Ph.Eur',
    type: 'Materiál',
    availableStock: 47692.24,
    moq: '28500g'
  },

  pentylenGlykol: {
    code: 'AKL011',
    name: 'Pentylen Glykol Green+',
    type: 'Materiál',
    availableStock: 80773.31,
    moq: '25000g'
  },

  sodaBicarbona: {
    code: 'AKL012',
    name: 'Soda Bicarbona',
    type: 'Materiál',
    availableStock: 86653.3,
    moq: '25000g'
  },

  // Products with margins data for testing
  darkovyBalicek: {
    code: 'DAR001',
    name: 'Dárkové balení',
    type: 'Produkt'
  },

  duvenyPanJasmin: {
    code: 'DEO001005',
    name: 'Důvěrný pan Jasmín 5ml',
    type: 'Produkt',
    availableStock: 67
  }
};

/**
 * Well-known purchase orders for testing
 * Note: Supplier names are anonymized for security reasons
 */
export const TestPurchaseOrders: Record<string, PurchaseOrder> = {
  // Draft order with single item
  draft1: {
    orderNumber: 'PO20251119-0855',
    supplier: 'Supplier A',
    orderDate: '19. 11. 2025',
    status: 'Návrh',
    hasInvoice: false,
    totalAmount: '38 323,20 Kč',
    itemCount: 1
  },

  // Draft order with multiple items
  draft2: {
    orderNumber: 'PO20251119-1128',
    supplier: 'Supplier B',
    orderDate: '19. 11. 2025',
    status: 'Návrh',
    hasInvoice: false,
    totalAmount: '77 380,28 Kč',
    itemCount: 3
  },

  // In transit - with invoice
  inTransitWithInvoice: {
    orderNumber: 'PO20251113-0850',
    supplier: 'Supplier C',
    orderDate: '13. 11. 2025',
    status: 'V přepravě',
    hasInvoice: true,
    totalAmount: '42 514,20 Kč',
    itemCount: 5
  },

  // In transit - without invoice
  inTransitNoInvoice: {
    orderNumber: 'PO20251113-1251',
    supplier: 'Supplier D',
    orderDate: '13. 11. 2025',
    status: 'V přepravě',
    hasInvoice: false,
    totalAmount: '65 880,00 Kč',
    itemCount: 2
  },

  // Edge case - zero amount order
  zeroAmount: {
    orderNumber: 'PO20251112-1051',
    supplier: 'Supplier E',
    orderDate: '12. 11. 2025',
    status: 'V přepravě',
    hasInvoice: false,
    totalAmount: '0,00 Kč',
    itemCount: 0
  }
};

/**
 * Well-known manufacturing orders for testing
 */
export const TestManufacturingOrders: Record<string, ManufacturingOrder> = {
  // Draft order for semi-product
  draftHedvabnyPan: {
    orderNumber: 'MO-2026-005',
    status: 'Návrh',
    productionDate: '24. 1. 2029', // Note: Future date anomaly
    product: 'Hedvábný pan Jasmín',
    productCode: 'MAS001001M',
    variantCount: 4
  }
};

/**
 * Well-known transport boxes for testing
 */
export const TestTransportBoxes: Record<string, TransportBox> = {
  // Open box
  openBox: {
    code: 'B999',
    state: 'Otevřený',
    itemCount: 0,
    lastUpdate: '08. 01. 2026 20:19'
  },

  // Stocked boxes with items
  stockedBox1: {
    code: 'B989',
    state: 'Naskladněný',
    itemCount: 1,
    lastUpdate: '08. 01. 2026 20:23'
  },

  stockedBox2: {
    code: 'B414',
    state: 'Naskladněný',
    itemCount: 1,
    lastUpdate: '05. 01. 2026 11:43'
  },

  // Multi-item boxes
  multiItemBox1: {
    code: 'B010',
    state: 'Naskladněný',
    itemCount: 2,
    lastUpdate: '19. 11. 2025 14:45'
  },

  multiItemBox2: {
    code: 'B033',
    state: 'Naskladněný',
    itemCount: 2,
    lastUpdate: '19. 11. 2025 09:40'
  },

  // Closed box
  closedBox: {
    code: 'B020',
    state: 'Uzavřený',
    itemCount: 1,
    lastUpdate: '19. 11. 2025 13:48'
  }
};

/**
 * Test supplier references
 * Note: Real supplier names are not stored in source code for security reasons
 * Use supplier order numbers to identify specific purchase orders instead
 */
export const TestSupplierPlaceholders = {
  supplierA: 'Supplier A',
  supplierB: 'Supplier B',
  supplierC: 'Supplier C',
  supplierD: 'Supplier D',
  supplierE: 'Supplier E'
} as const;

/**
 * Expected dashboard statistics (approximate)
 */
export const ExpectedDashboardStats = {
  catalogTotalItems: 906,
  purchaseOrdersActive: 8,
  transportBoxesTotal: 2902,
  transportBoxesActive: 837,
  transportBoxesStocked: 708,
  transportBoxesClosed: 2065,

  // Inventory age distribution (products)
  productsInventoryUnder180Days: 201,
  productsInventory180to365Days: 163,
  productsInventoryOver365Days: 0,

  // Inventory age distribution (raw materials)
  materialsInventoryUnder180Days: 143,
  materialsInventory180to365Days: 5,
  materialsInventoryOver365Days: 58,

  // Critical stock
  criticalStockMaterials: 7
} as const;

/**
 * Helper function to verify test data exists
 * Use this at the beginning of tests to fail fast if data is missing
 */
export function requireTestData<T>(
  data: T | undefined,
  dataDescription: string
): T {
  if (!data) {
    throw new Error(
      `Required test data missing: ${dataDescription}. ` +
      `Please check if test fixtures in docs/testing/test-data-fixtures.md are still valid.`
    );
  }
  return data;
}

/**
 * Helper function to assert minimum count
 * Use this to verify data collection has minimum expected items
 */
export function assertMinimumCount(
  actual: number,
  minimum: number,
  description: string
): void {
  if (actual < minimum) {
    throw new Error(
      `Insufficient test data: Expected at least ${minimum} ${description}, ` +
      `but found only ${actual}. Test data may have been cleared or modified.`
    );
  }
}
