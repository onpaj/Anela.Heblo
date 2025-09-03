import { 
  PurchaseOrderLineDto,
  SupplierDto,
  CreatePurchaseOrderLineRequest,
  UpdatePurchaseOrderLineRequest,
  CreatePurchaseOrderRequest,
  UpdatePurchaseOrderRequest
} from '../../../api/generated/api-client';
import { MaterialForPurchaseDto } from '../../../api/hooks/useMaterials';
import { FormData } from './PurchaseOrderTypes';

// Generate default order number in format POyyyyMMdd-HHmm
export const generateDefaultOrderNumber = (): string => {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  const hour = String(now.getHours()).padStart(2, '0');
  const minute = String(now.getMinutes()).padStart(2, '0');
  
  return `PO${year}${month}${day}-${hour}${minute}`;
};

// Create a default empty line for the form
export const createDefaultLine = (): PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null } => {
  return Object.assign(new PurchaseOrderLineDto(), {
    id: 0, // Temporary ID for new lines
    materialId: `temp-${Date.now()}`,
    materialName: '',
    quantity: 1,
    unitPrice: 0,
    lineTotal: 0,
    selectedMaterial: undefined
  });
};

// Create initial form data for create mode
export const createInitialFormData = (): FormData => ({
  orderNumber: generateDefaultOrderNumber(),
  selectedSupplier: null,
  orderDate: new Date().toISOString().split('T')[0], // Today's date
  expectedDeliveryDate: '',
  contactVia: null,
  notes: '',
  lines: [createDefaultLine()]
});

// Calculate line total with proper rounding
export const calculateLineTotal = (quantity: number, unitPrice: number): number => {
  return Math.round((quantity || 0) * (unitPrice || 0) * 100) / 100;
};

// Round unit price to 4 decimal places
export const roundUnitPrice = (price: number): number => {
  return Math.round(price * 10000) / 10000;
};

// Calculate total from all lines
export const calculateTotal = (lines: (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[]): number => {
  return lines.reduce((sum, line) => sum + (line.lineTotal || 0), 0);
};

// Filter valid lines for submission (has material, quantity, and price)
export const getValidLines = (lines: (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[]) => {
  return lines.filter(line => {
    const hasValidMaterial = line.selectedMaterial && line.selectedMaterial.productName;
    return hasValidMaterial && line.quantity && line.unitPrice;
  });
};

// Create purchase order request for new orders
export const createPurchaseOrderRequest = (formData: FormData): CreatePurchaseOrderRequest => {
  const validLines = getValidLines(formData.lines);
  
  return new CreatePurchaseOrderRequest({
    supplierId: formData.selectedSupplier?.id || 0,
    orderDate: formData.orderDate,
    expectedDeliveryDate: formData.expectedDeliveryDate || undefined,
    contactVia: formData.contactVia || undefined,
    notes: formData.notes || undefined,
    orderNumber: formData.orderNumber || undefined,
    lines: validLines.map(line => new CreatePurchaseOrderLineRequest({
      materialId: line.selectedMaterial?.productCode || 'MANUAL',
      name: line.selectedMaterial?.productName || '',
      quantity: line.quantity!,
      unitPrice: line.unitPrice!,
      notes: line.notes
    }))
  });
};

// Create purchase order update request for existing orders
export const updatePurchaseOrderRequest = (formData: FormData, editOrderId: number): UpdatePurchaseOrderRequest => {
  const validLines = getValidLines(formData.lines);
  
  return new UpdatePurchaseOrderRequest({
    id: editOrderId,
    supplierId: formData.selectedSupplier?.id || 0,
    expectedDeliveryDate: formData.expectedDeliveryDate ? new Date(formData.expectedDeliveryDate) : undefined,
    contactVia: formData.contactVia || undefined,
    notes: formData.notes || undefined,
    orderNumber: formData.orderNumber || undefined,
    lines: validLines.map(line => new UpdatePurchaseOrderLineRequest({
      materialId: line.selectedMaterial?.productCode || 'MANUAL',
      name: line.selectedMaterial?.productName || '',
      quantity: line.quantity!,
      unitPrice: line.unitPrice!,
      notes: line.notes
    }))
  });
};

// Transform existing order data for editing
export const transformExistingOrderData = (existingOrderData: any): FormData => {
  const linesWithMaterials = existingOrderData.lines && existingOrderData.lines.length > 0
    ? existingOrderData.lines.map((line: any) => Object.assign(new PurchaseOrderLineDto(), {
        ...line,
        selectedMaterial: undefined // Will be resolved by MaterialResolver components
      }))
    : [createDefaultLine()];

  // Create supplier object from existing data
  const existingSupplier = new SupplierDto({
    id: existingOrderData.supplierId || 0,
    name: existingOrderData.supplierName || '',
    code: 'UNKNOWN', // We don't have supplier code in existing data
  });

  return {
    orderNumber: existingOrderData.orderNumber || generateDefaultOrderNumber(),
    selectedSupplier: existingSupplier,
    orderDate: existingOrderData.orderDate ? new Date(existingOrderData.orderDate).toISOString().split('T')[0] : new Date().toISOString().split('T')[0],
    expectedDeliveryDate: existingOrderData.expectedDeliveryDate ? new Date(existingOrderData.expectedDeliveryDate).toISOString().split('T')[0] : '',
    contactVia: existingOrderData.contactVia || null,
    notes: existingOrderData.notes || '',
    lines: linesWithMaterials
  };
};