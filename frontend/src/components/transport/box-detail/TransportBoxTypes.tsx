import {
  TransportBoxDto,
  CatalogItemDto,
} from "../../../api/generated/api-client";
import { LastAddedItem } from "../../../api/hooks/useLastAddedItem";

export interface TransportBoxDetailProps {
  boxId: number | null;
  isOpen: boolean;
  onClose: () => void;
}

export interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

export interface TransportBoxInfoProps {
  transportBox: TransportBoxDto;
  descriptionInput: string;
  handleDescriptionChange: (value: string) => void;
  isDescriptionChanged: boolean;
  isFormEditable: (fieldType: "items" | "notes" | "boxNumber") => boolean;
  formatDate: (dateString: string | Date | undefined) => string;
}

export interface TransportBoxItemsProps {
  transportBox: TransportBoxDto;
  isFormEditable: (fieldType: "items" | "notes" | "boxNumber") => boolean;
  formatDate: (dateString: string | Date | undefined) => string;
  handleRemoveItem: (itemId: number) => void;
  quantityInput: string;
  setQuantityInput: (value: string) => void;
  selectedProduct: CatalogItemDto | null;
  setSelectedProduct: (product: CatalogItemDto | null) => void;
  handleAddItem: () => void;
  // Quick add last item functionality
  lastAddedItem: LastAddedItem | null;
  handleQuickAdd: () => void;
}

export interface TransportBoxHistoryProps {
  transportBox: TransportBoxDto;
  formatDate: (dateString: string | Date | undefined) => string;
}

export interface TransportBoxActionsProps {
  transportBox: TransportBoxDto;
  changeStateMutation: any;
  handleStateChange: (newStateString: string) => void;
}

export interface TransportBoxModalsProps {
  transportBox: TransportBoxDto;
  isAddItemModalOpen: boolean;
  setIsAddItemModalOpen: (value: boolean) => void;
  isLocationSelectionModalOpen: boolean;
  setIsLocationSelectionModalOpen: (value: boolean) => void;
  handleAddItemSuccess: () => void;
  handleLocationSelectionSuccess: () => void;
}

export interface TransportBoxStateBadgeProps {
  state: string;
  size?: "sm" | "md" | "lg";
}

// State labels mapping - using enum keys
export const stateLabels: Record<string, string> = {
  New: "Nový",
  Opened: "Otevřený",
  InTransit: "V přepravě",
  Received: "Přijatý",
  Stocked: "Naskladněný",
  Reserve: "V rezervě",
  Closed: "Uzavřený",
  Error: "Chyba",
};

export const stateColors: Record<string, string> = {
  New: "bg-gray-100 text-gray-800",
  Opened: "bg-blue-100 text-blue-800",
  InTransit: "bg-yellow-100 text-yellow-800",
  Received: "bg-purple-100 text-purple-800",
  Stocked: "bg-green-100 text-green-800",
  Reserve: "bg-indigo-100 text-indigo-800",
  Closed: "bg-gray-100 text-gray-800",
  Error: "bg-red-100 text-red-800",
};
