import { 
  PurchaseOrderLineDto, 
  ContactVia,
  SupplierDto
} from '../../../api/generated/api-client';
import { MaterialForPurchaseDto } from '../../../api/hooks/useMaterials';

export interface PurchaseOrderFormProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess?: (orderId: number) => void;
  editOrderId?: number; // Optional - if provided, form is in edit mode
}

export interface FormData {
  orderNumber: string;
  selectedSupplier: SupplierDto | null;
  orderDate: string;
  expectedDeliveryDate: string;
  contactVia: ContactVia | null;
  notes: string;
  lines: (PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null })[];
}

export interface MaterialResolverProps {
  materialId: string;
  lineIndex: number;
  onMaterialResolved: (index: number, material: MaterialForPurchaseDto | null) => void;
}

export interface PurchaseOrderHeaderProps {
  formData: FormData;
  errors: Record<string, string>;
  onInputChange: (field: keyof FormData, value: string | ContactVia | null | SupplierDto) => void;
  onSupplierSelect: (supplier: SupplierDto | null) => void;
}

export interface PurchaseOrderLinesProps {
  formData: FormData;
  errors: Record<string, string>;
  onAddLine: () => void;
  onRemoveLine: (index: number) => void;
  onUpdateLine: (index: number, field: keyof PurchaseOrderLineDto, value: string | number) => void;
  onMaterialSelect: (index: number, material: MaterialForPurchaseDto | null) => void;
}

export interface PurchaseOrderLineItemProps {
  line: PurchaseOrderLineDto & { selectedMaterial?: MaterialForPurchaseDto | null };
  index: number;
  errors: Record<string, string>;
  onUpdateLine: (index: number, field: keyof PurchaseOrderLineDto, value: string | number) => void;
  onMaterialSelect: (index: number, material: MaterialForPurchaseDto | null) => void;
  onRemoveLine: (index: number) => void;
}