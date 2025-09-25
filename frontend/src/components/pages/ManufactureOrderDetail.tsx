import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import i18n from "../../i18n";
import { useParams, useNavigate, Navigate } from "react-router-dom";
import {
  X,
  Calendar,
  User,
  Loader2,
  AlertCircle,
  Edit,
  Info,
  ScrollText,
  StickyNote,
  Factory,
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
  Save,
  XCircle,
  Hash,
  CalendarClock,
  Ban,
  Copy,
  Maximize2,
} from "lucide-react";
import {
  useManufactureOrderDetailQuery,
  useUpdateManufactureOrder,
  useUpdateManufactureOrderStatus,
  useConfirmSemiProductManufacture,
  useConfirmProductCompletion,
  useDuplicateManufactureOrder,
} from "../../api/hooks/useManufactureOrders";
import { useQueryClient } from "@tanstack/react-query";
import {
  UpdateManufactureOrderRequest,
  UpdateManufactureOrderStatusRequest,
  UpdateManufactureOrderProductRequest,
  UpdateManufactureOrderSemiProductRequest,
  ConfirmSemiProductManufactureRequest,
  ConfirmProductCompletionRequest,
  ManufactureOrderState,
} from "../../api/generated/api-client";
import ResponsiblePersonCombobox from "../common/ResponsiblePersonCombobox";
import ConfirmSemiProductQuantityModal from "../modals/ConfirmSemiProductQuantityModal";
import ConfirmProductCompletionModal from "../modals/ConfirmProductCompletionModal";
import ResolveManualActionModal from "../modals/ResolveManualActionModal";

interface ManufactureOrderDetailProps {
  orderId?: number;
  isOpen?: boolean;
  onClose?: () => void;
  onEdit?: (orderId: number) => void;
}


const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800",
  [ManufactureOrderState.Planned]: "bg-blue-100 text-blue-800",
  [ManufactureOrderState.SemiProductManufactured]: "bg-yellow-100 text-yellow-800",
  [ManufactureOrderState.Completed]: "bg-green-100 text-green-800",
  [ManufactureOrderState.Cancelled]: "bg-red-100 text-red-800",
};

const ManufactureOrderDetail: React.FC<ManufactureOrderDetailProps> = ({
  orderId: propOrderId,
  isOpen = true,
  onClose,
  onEdit,
}) => {
  const { t } = useTranslation();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  
  // Determine if this is modal mode (has propOrderId) or page mode (uses URL param)
  const isModalMode = propOrderId !== undefined;
  const urlOrderId = id ? parseInt(id, 10) : 0;
  const orderId = isModalMode ? propOrderId : urlOrderId;
  
  // For page mode, create a close handler that navigates back
  const handleClose = useCallback(() => {
    // Invalidate queries to refresh data in lists and calendar
    queryClient.invalidateQueries({
      queryKey: ["manufacture-orders"]
    });
    queryClient.invalidateQueries({
      queryKey: ["manufactureOrders", "calendar"]
    });

    if (onClose) {
      onClose();
    } else {
      navigate("/manufacturing/orders");
    }
  }, [onClose, navigate, queryClient]);

  // We'll define handleCloseWithWeekNavigation after order is loaded
  
  // Helper function to get translated state label
  const getStateLabel = (state: ManufactureOrderState): string => {
    // For string enums, the state value is the string itself
    const stateKey = state.toString();
    const translationKey = `manufacture.states.${stateKey}`;
    const translated = t(translationKey);
    
    // Debug: log translation details
    console.log('Translation debug:', {
      state,
      stateKey,
      translationKey,
      translated,
      currentLang: i18n.language,
      exists: i18n.exists(translationKey)
    });
    
    return translated || stateKey;
  };

  // Helper function to get the order's planned date for navigation
  const getOrderPlannedDate = (order: any): Date => {
    // Priority: semiProductPlannedDate > productPlannedDate > today
    if (order.semiProductPlannedDate) {
      return order.semiProductPlannedDate instanceof Date 
        ? order.semiProductPlannedDate 
        : new Date(order.semiProductPlannedDate);
    }
    if (order.productPlannedDate) {
      return order.productPlannedDate instanceof Date 
        ? order.productPlannedDate 
        : new Date(order.productPlannedDate);
    }
    return new Date();
  };
  
  // Helper function to get translated audit action label
  const getAuditActionLabel = (action: any): string => {
    const actionName = typeof action === 'string' ? action : action?.toString();
    return t(`manufacture.auditActions.${actionName}`) || actionName || '-';
  };

  // Helper function to check if text needs truncation (more than ~100 characters for 2 lines)
  const shouldTruncateText = (text: string): boolean => {
    return text.length > 100;
  };

  // Helper function to truncate text for 2 lines display
  const truncateText = (text: string): string => {
    if (text.length <= 100) return text;
    return text.substring(0, 97) + '...';
  };

  // Handle expand note
  const handleExpandNote = (noteText: string) => {
    setExpandedNoteContent(noteText);
    setShowExpandedNote(true);
  };
  
  // Tab state
  const [activeTab, setActiveTab] = useState<"info" | "notes" | "log">("info");
  
  // Note input state
  const [newNote, setNewNote] = useState("");

  // Editable fields state - always in edit mode
  const [editableResponsiblePerson, setEditableResponsiblePerson] = useState("");
  const [editableSemiProductDate, setEditableSemiProductDate] = useState("");
  const [editableProductDate, setEditableProductDate] = useState("");
  const [editableSemiProductQuantity, setEditableSemiProductQuantity] = useState("");
  const [editableProductQuantities, setEditableProductQuantities] = useState<Record<number, string>>({});
  const [editableLotNumber, setEditableLotNumber] = useState("");
  const [editableExpirationDate, setEditableExpirationDate] = useState("");
  const [editableErpOrderNumberSemiproduct, setEditableErpOrderNumberSemiproduct] = useState("");
  const [editableErpOrderNumberProduct, setEditableErpOrderNumberProduct] = useState("");
  // Confirmation dialog state
  const [showCancelConfirmation, setShowCancelConfirmation] = useState(false);
  // Semi-product quantity confirmation modal state
  const [showQuantityConfirmModal, setShowQuantityConfirmModal] = useState(false);
  // Product completion confirmation modal state
  const [showProductCompletionModal, setShowProductCompletionModal] = useState(false);
  // Resolve manual action modal state
  const [showResolveModal, setShowResolveModal] = useState(false);
  // Expanded note modal state
  const [showExpandedNote, setShowExpandedNote] = useState(false);
  const [expandedNoteContent, setExpandedNoteContent] = useState("");

  // Fetch order details
  const {
    data: orderData,
    isLoading: orderLoading,
    error: orderError,
  } = useManufactureOrderDetailQuery(orderId || 0);

  const order = orderData?.order;

  // Handle close with navigation to specific week
  const handleCloseWithWeekNavigation = useCallback(() => {
    // Invalidate queries to refresh data in lists and calendar
    queryClient.invalidateQueries({
      queryKey: ["manufacture-orders"]
    });
    queryClient.invalidateQueries({
      queryKey: ["manufactureOrders", "calendar"]
    });

    if (onClose) {
      onClose();
    } else {
      // Navigate to weekly calendar with the order's week
      if (order) {
        // Use the updated dates from the form if available
        const updatedSemiProductDate = editableSemiProductDate ? new Date(editableSemiProductDate) : null;
        const updatedProductDate = editableProductDate ? new Date(editableProductDate) : null;
        const orderDate = updatedSemiProductDate || updatedProductDate || getOrderPlannedDate(order);
        const dateString = orderDate.toISOString().split('T')[0];
        
        console.log('🔍 Navigation debug při Close:', {
          editableSemiProductDate,
          editableProductDate,
          updatedSemiProductDate,
          updatedProductDate,
          originalOrderData: {
            semiProductPlannedDate: order.semiProductPlannedDate,
            productPlannedDate: order.productPlannedDate
          },
          finalOrderDate: orderDate,
          finalDateString: dateString,
          navigationUrl: `/manufacturing/orders?view=weekly&date=${dateString}`
        });
        
        navigate(`/manufacturing/orders?view=weekly&date=${dateString}`);
      } else {
        navigate("/manufacturing/orders");
      }
    }
  }, [onClose, navigate, queryClient, order, editableSemiProductDate, editableProductDate]);

  // Mutations
  const updateOrderMutation = useUpdateManufactureOrder();
  const updateOrderStatusMutation = useUpdateManufactureOrderStatus();
  const confirmSemiProductMutation = useConfirmSemiProductManufacture();
  const confirmProductCompletionMutation = useConfirmProductCompletion();
  const duplicateOrderMutation = useDuplicateManufactureOrder();

  // Initialize editable fields when order data changes
  React.useEffect(() => {
    if (order) {
      console.log('🔧 Inicializace polí z order dat:', {
        orderSemiProductPlannedDate: order.semiProductPlannedDate,
        orderProductPlannedDate: order.productPlannedDate,
        responsiblePerson: order.responsiblePerson
      });
      
      // Check if fields can be edited based on state
      const fieldsCanBeEdited = order.state === ManufactureOrderState.Draft || order.state === ManufactureOrderState.Planned;
      
      setEditableResponsiblePerson(order.responsiblePerson || "");
      
      const semiDateString = order.semiProductPlannedDate ? new Date(order.semiProductPlannedDate).toISOString().split('T')[0] : "";
      const productDateString = order.productPlannedDate ? new Date(order.productPlannedDate).toISOString().split('T')[0] : "";
      
      console.log('🔧 Nastavované hodnoty datumů:', {
        semiDateString,
        productDateString
      });
      
      setEditableSemiProductDate(semiDateString);
      setEditableProductDate(productDateString);
      // For semi-product: always edit planned quantity in edit mode, show actual quantity in read-only mode
      const semiProductQuantity = fieldsCanBeEdited 
        ? order.semiProduct?.plannedQuantity?.toString() || ""
        : order.semiProduct?.actualQuantity?.toString() || order.semiProduct?.plannedQuantity?.toString() || "";
      setEditableSemiProductQuantity(semiProductQuantity);
      
      // Initialize lot number and expiration date
      setEditableLotNumber(order.semiProduct?.lotNumber || "");
      setEditableExpirationDate(order.semiProduct?.expirationDate ? new Date(order.semiProduct.expirationDate).toISOString().split('T')[0] : "");
      
      // Initialize ERP order numbers
      setEditableErpOrderNumberSemiproduct(order.erpOrderNumberSemiproduct || "");
      setEditableErpOrderNumberProduct(order.erpOrderNumberProduct || "");
      
      // Initialize product quantities - edit planned quantity in edit mode, show actual quantity in read-only mode
      const productQuantities: Record<number, string> = {};
      order.products?.forEach((product, index) => {
        const quantity = fieldsCanBeEdited 
          ? product.plannedQuantity?.toString() || ""
          : product.actualQuantity?.toString() || product.plannedQuantity?.toString() || "";
        productQuantities[index] = quantity;
      });
      setEditableProductQuantities(productQuantities);
    }
  }, [order]);

  // Helper function to get ISO week number
  const getWeekNumber = (date: Date): number => {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const dayNum = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(),0,1));
    return Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1)/7);
  };

  // Auto-calculate lot number and expiration date when semi-product planned date changes
  React.useEffect(() => {
    if (editableSemiProductDate) {
      const semiProductDate = new Date(editableSemiProductDate);
      
      // Auto-set lot number as "wwyyyyMM" format - always recalculate when date changes
      const year = semiProductDate.getFullYear();
      const month = String(semiProductDate.getMonth() + 1).padStart(2, '0');
      const week = String(getWeekNumber(semiProductDate)).padStart(2, '0');
      const newLotNumber = `${week}${year}${month}`;
      
      // Always update lot number when date changes
      setEditableLotNumber(newLotNumber);
      
      // Auto-set expiration date using ExpirationMonths from semi-product + date from order
      const expirationMonths = order?.semiProduct?.expirationMonths || 12; // Default to 12 months if not set
      const expirationDate = new Date(semiProductDate);
      expirationDate.setMonth(expirationDate.getMonth() + expirationMonths);
      
      // Set to last day of the expiration month
      const lastDayOfExpirationMonth = new Date(expirationDate.getFullYear(), expirationDate.getMonth() + 1, 0).getDate();
      expirationDate.setDate(lastDayOfExpirationMonth);
      
      const newExpirationDateString = expirationDate.toISOString().split('T')[0];
      
      // Always update expiration date when order date changes
      setEditableExpirationDate(newExpirationDateString);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editableSemiProductDate, order?.semiProduct?.expirationMonths]);

  // Add keyboard event listener for Esc key
  React.useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen) {
        handleClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
    }

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, handleClose]);

  // For page mode, redirect if invalid ID (after all hooks)
  if (!isModalMode && (!id || isNaN(urlOrderId) || urlOrderId <= 0)) {
    return <Navigate to="/manufacturing/orders" replace />;
  }

  if (!isOpen) {
    return null;
  }

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget && isModalMode) {
      handleClose();
    }
  };

  // Format datetime
  const formatDateTime = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleString("cs-CZ");
  };

  // Format date
  const formatDate = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleDateString("cs-CZ");
  };


  // State transitions logic
  const getStateTransitions = (currentState: ManufactureOrderState) => {
    const transitions = {
      [ManufactureOrderState.Draft]: {
        next: ManufactureOrderState.Planned,
        previous: null,
      },
      [ManufactureOrderState.Planned]: {
        next: ManufactureOrderState.SemiProductManufactured,
        previous: ManufactureOrderState.Draft,
      },
      [ManufactureOrderState.SemiProductManufactured]: {
        next: ManufactureOrderState.Completed,
        previous: ManufactureOrderState.Planned,
      },
      [ManufactureOrderState.Completed]: {
        next: null,
        previous: ManufactureOrderState.SemiProductManufactured,
      },
      [ManufactureOrderState.Cancelled]: {
        next: null,
        previous: null,
      },
    };
    
    return transitions[currentState] || { next: null, previous: null };
  };

  // Handle state change
  const handleStateChange = async (newState: ManufactureOrderState) => {
    if (!order || !orderId) return;

    // Special handling for transition from Planned to SemiProductManufactured
    if (order.state === ManufactureOrderState.Planned && newState === ManufactureOrderState.SemiProductManufactured) {
      setShowQuantityConfirmModal(true);
      return;
    }

    // Special handling for transition from SemiProductManufactured to Completed
    if (order.state === ManufactureOrderState.SemiProductManufactured && newState === ManufactureOrderState.Completed) {
      setShowProductCompletionModal(true);
      return;
    }

    try {
      const request = new UpdateManufactureOrderStatusRequest({
        id: orderId,
        newState,
        changeReason: `Změna stavu z ${order.state !== undefined ? getStateLabel(order.state) : 'Neznámý'} na ${getStateLabel(newState)}`,
      });
      await updateOrderStatusMutation.mutateAsync(request);
    } catch (error) {
      console.error("Error updating order status:", error);
      // TODO: Show error notification to user
    }
  };

  // Handle saving editable fields
  const handleSave = async () => {
    if (!order || !orderId) return;

    try {
      // Prepare products data - include IDs to update existing products instead of replacing them
      // Only send quantity updates if fields are editable, otherwise send null to preserve existing values
      const products = order.products?.map((product, index) => new UpdateManufactureOrderProductRequest({
        id: product.id, // Include ID to update existing product
        productCode: product.productCode || "",
        productName: product.productName || "",
        // Only send PlannedQuantity if fields are editable, otherwise undefined to preserve existing values
        plannedQuantity: canEditFields ? (parseFloat(editableProductQuantities[index] || "0") || 0) : undefined,
      })) || [];

      const semiProductRequest = (editableLotNumber || editableExpirationDate || (canEditFields && editableSemiProductQuantity)) ? new UpdateManufactureOrderSemiProductRequest({
        plannedQuantity: canEditFields && editableSemiProductQuantity ? parseFloat(editableSemiProductQuantity) || undefined : undefined,
        lotNumber: editableLotNumber || undefined,
        expirationDate: editableExpirationDate ? (() => {
          const [year, month] = editableExpirationDate.split('-').map(Number);
          return new Date(year, month, 0); // 0th day of next month = last day of current month
        })() : undefined,
      }) : undefined;

      console.log('Saving order data:', {
        canEditFields,
        editableLotNumber,
        editableExpirationDate,
        editableProductQuantities,
        products: products.map(p => ({ 
          id: p.id, 
          plannedQuantity: p.plannedQuantity,
          productCode: p.productCode 
        })),
        semiProductRequest
      });

      const request = new UpdateManufactureOrderRequest({
        id: orderId,
        semiProductPlannedDate: editableSemiProductDate ? new Date(editableSemiProductDate) : (order.semiProductPlannedDate ? new Date(order.semiProductPlannedDate) : new Date()),
        productPlannedDate: editableProductDate ? new Date(editableProductDate) : (order.productPlannedDate ? new Date(order.productPlannedDate) : new Date()),
        responsiblePerson: editableResponsiblePerson || undefined,
        erpOrderNumberSemiproduct: editableErpOrderNumberSemiproduct || undefined,
        erpOrderNumberProduct: editableErpOrderNumberProduct || undefined,
        semiProduct: semiProductRequest,
        products,
        newNote: newNote.trim() || undefined,
      });
      await updateOrderMutation.mutateAsync(request);

      // Clear the note input after saving
      if (newNote.trim()) {
        setNewNote("");
      }
      
      // Navigate to weekly calendar with the order's week
      // Use the updated dates from the form, not the original order data
      const updatedSemiProductDate = editableSemiProductDate ? new Date(editableSemiProductDate) : null;
      const updatedProductDate = editableProductDate ? new Date(editableProductDate) : null;
      const orderDate = updatedSemiProductDate || updatedProductDate || getOrderPlannedDate(order);
      const dateString = orderDate.toISOString().split('T')[0];
      
      console.log('🔍 Navigation debug při Save:', {
        editableSemiProductDate,
        editableProductDate,
        updatedSemiProductDate,
        updatedProductDate,
        originalOrderData: {
          semiProductPlannedDate: order.semiProductPlannedDate,
          productPlannedDate: order.productPlannedDate
        },
        finalOrderDate: orderDate,
        finalDateString: dateString,
        navigationUrl: `/manufacturing/orders?view=weekly&date=${dateString}`
      });
      
      if (onClose) {
        // Modal mode - close and let parent handle navigation
        onClose();
      } else {
        // Page mode - navigate directly to weekly calendar with the specific week
        navigate(`/manufacturing/orders?view=weekly&date=${dateString}`);
      }
    } catch (error) {
      console.error("Error updating order:", error);
      // TODO: Show error notification to user
    }
  };

  // Handle product quantity change
  const handleProductQuantityChange = (index: number, value: string) => {
    setEditableProductQuantities(prev => ({
      ...prev,
      [index]: value
    }));
  };

  // Handle duplicate order
  const handleDuplicate = async () => {
    if (!orderId) return;

    try {
      const result = await duplicateOrderMutation.mutateAsync(orderId);
      
      // Navigate to the new duplicated order detail
      if (result.id) {
        const newOrderUrl = `/manufacturing/orders/${result.id}`;
        
        if (onEdit && onClose) {
          // Modal mode - tell parent to open the new order detail
          onEdit(result.id);
        } else {
          // Page mode - navigate directly to the new duplicated order
          navigate(newOrderUrl);
        }
      } else {
        console.error('No ID returned from duplication:', result);
      }
    } catch (error) {
      console.error("Error duplicating order:", error);
      // TODO: Show error notification to user
    }
  };

  // Handle semi-product quantity confirmation
  const handleConfirmQuantity = async (request: ConfirmSemiProductManufactureRequest) => {
    try {
      await confirmSemiProductMutation.mutateAsync(request);
      setShowQuantityConfirmModal(false);
      // Close the entire ManufactureOrderDetail dialog after successful confirmation
      handleCloseWithWeekNavigation();
    } catch (error) {
      console.error("Error confirming semi-product quantity:", error);
      // Error is handled by the modal component
      throw error;
    }
  };

  const handleConfirmProductCompletion = async (request: ConfirmProductCompletionRequest) => {
    try {
      await confirmProductCompletionMutation.mutateAsync(request);
      setShowProductCompletionModal(false);
      // Close the entire ManufactureOrderDetail dialog after successful confirmation
      handleCloseWithWeekNavigation();
    } catch (error) {
      console.error("Error confirming product completion:", error);
      // Error is handled by the modal component
      throw error;
    }
  };

  // Check if fields can be edited based on state - only draft and planned
  const canEditFields = order?.state === ManufactureOrderState.Draft || order?.state === ManufactureOrderState.Planned;

  const currentStateTransitions = order?.state !== undefined ? getStateTransitions(order.state) : { next: null, previous: null };

  const content = (
    <div className={`bg-white ${isModalMode ? 'rounded-lg shadow-xl' : ''} ${isModalMode ? 'max-w-7xl w-full max-h-[720px]' : 'h-full max-w-7xl'} overflow-hidden flex flex-col relative`}>
        {/* Header */}
        <div className="flex items-center justify-between p-4 border-b border-gray-200 flex-shrink-0">
          <div className="flex items-center space-x-3">
            <Factory className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                {order
                  ? `Výrobní zakázka ${order.orderNumber}`
                  : "Načítání..."}
              </h2>
              <p className="text-sm text-gray-500">ID: {orderId}</p>
            </div>
          </div>
          <div className="flex items-center space-x-2">
            {/* Previous State Button */}
            {order && order.state !== ManufactureOrderState.Cancelled && currentStateTransitions.previous !== null && (
              <button
                onClick={() => handleStateChange(currentStateTransitions.previous!)}
                disabled={updateOrderStatusMutation.isPending}
                className="flex items-center px-4 py-3 bg-gray-500 text-white rounded-lg hover:bg-gray-600 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed border-2 border-gray-500 hover:border-gray-600"
                title={`Zpět na: ${getStateLabel(currentStateTransitions.previous!)}`}
              >
                {updateOrderStatusMutation.isPending ? (
                  <Loader2 className="h-4 w-4 mr-1 animate-spin" />
                ) : (
                  <ChevronLeft className="h-4 w-4 mr-1" />
                )}
                {getStateLabel(currentStateTransitions.previous!)}
              </button>
            )}
            
            {/* Current State Display - Always visible */}
            {order && order.state !== undefined && (
              <div className="flex items-center px-4 py-2 bg-gray-100 border-2 border-gray-300 rounded-lg">
                <span className={`inline-flex items-center px-3 py-1 rounded-full text-sm font-medium ${stateColors[order.state]}`}>
                  {getStateLabel(order.state)}
                </span>
              </div>
            )}
            
            {/* Next State Button */}
            {order && order.state !== ManufactureOrderState.Cancelled && currentStateTransitions.next !== null && (
              <button
                onClick={() => handleStateChange(currentStateTransitions.next!)}
                disabled={updateOrderStatusMutation.isPending}
                className="flex items-center px-4 py-3 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed border-2 border-indigo-600 hover:border-indigo-700"
                title={`Pokračovat na: ${getStateLabel(currentStateTransitions.next!)}`}
              >
                {updateOrderStatusMutation.isPending ? (
                  <Loader2 className="h-4 w-4 mr-1 animate-spin" />
                ) : (
                  <>
                    {getStateLabel(currentStateTransitions.next!)}
                    <ChevronRight className="h-4 w-4 ml-1" />
                  </>
                )}
              </button>
            )}
            {onEdit && order && (
              <button
                onClick={() => onEdit(orderId)}
                className="text-gray-400 hover:text-indigo-600 transition-colors"
                title="Upravit zakázku"
              >
                <Edit className="h-5 w-5" />
              </button>
            )}
            <button
              onClick={handleCloseWithWeekNavigation}
              className="text-gray-400 hover:text-gray-600 transition-colors"
              title={isModalMode ? "Zavřít" : "Zpět na seznam"}
            >
              {isModalMode ? (
                <X className="h-6 w-6" />
              ) : (
                <ArrowLeft className="h-6 w-6" />
              )}
            </button>
          </div>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-hidden flex flex-col min-h-0">
          {orderLoading ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2">
                <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                <div className="text-gray-500">
                  Načítání detailů zakázky...
                </div>
              </div>
            </div>
          ) : orderError ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2 text-red-600">
                <AlertCircle className="h-5 w-5" />
                <div>Chyba při načítání detailů: {orderError.message}</div>
              </div>
            </div>
          ) : order ? (
            <>
              {/* Tabs */}
              <div className="border-b border-gray-200 px-4 pt-3">
                <nav className="-mb-px flex space-x-8">
                  <button
                    onClick={() => setActiveTab("info")}
                    className={`${
                      activeTab === "info"
                        ? "border-indigo-500 text-indigo-600"
                        : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <Info className="h-4 w-4 mr-2" />
                    Základní informace
                  </button>
                  <button
                    onClick={() => setActiveTab("notes")}
                    className={`${
                      activeTab === "notes"
                        ? "border-indigo-500 text-indigo-600"
                        : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <StickyNote className="h-4 w-4 mr-2" />
                    Poznámky ({order.notes?.length || 0})
                  </button>
                  <button
                    onClick={() => setActiveTab("log")}
                    className={`${
                      activeTab === "log"
                        ? "border-indigo-500 text-indigo-600"
                        : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <ScrollText className="h-4 w-4 mr-2" />
                    Audit log ({order.auditLog?.length || 0})
                  </button>
                </nav>
              </div>

              {/* Tab Content */}
              <div className="overflow-y-auto p-3">
                {activeTab === "info" && (
                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                    {/* Left Panel - Basic Info, Time Data, and Notes */}
                    <div className="flex flex-col space-y-4">
                      {/* Basic Information */}
                      <div className="bg-gray-50 rounded-lg p-3">
                        <h3 className="text-base font-semibold text-gray-800 mb-3 flex items-center">
                          <Info className="h-4 w-4 mr-2 text-indigo-600" />
                          Základní informace
                        </h3>
                        <div className="space-y-2">
                          <div className="flex items-center justify-between">
                            <div className="flex items-center">
                              <User className="h-4 w-4 text-gray-400 mr-2" />
                              <span className="text-sm text-gray-500">Odpovědná osoba:</span>
                            </div>
                            {canEditFields ? (
                              <div className="w-48">
                                <ResponsiblePersonCombobox
                                  value={editableResponsiblePerson}
                                  onChange={(value) => setEditableResponsiblePerson(value || "")}
                                  placeholder="Vyberte..."
                                  allowManualEntry={true}
                                />
                              </div>
                            ) : (
                              <span className="text-sm text-gray-900">
                                {order.responsiblePerson || "Není přiřazena"}
                              </span>
                            )}
                          </div>
                          
                          {/* ERP Order Numbers */}
                          <div className="flex items-center justify-between">
                            <div className="flex items-center">
                              <Hash className="h-4 w-4 text-gray-400 mr-2" />
                              <span className="text-sm text-gray-500">ERP č. (meziprod.):</span>
                            </div>
                            {canEditFields ? (
                              <input
                                type="text"
                                value={editableErpOrderNumberSemiproduct}
                                onChange={(e) => setEditableErpOrderNumberSemiproduct(e.target.value)}
                                className="w-48 text-sm border border-gray-300 rounded px-2 py-1"
                                placeholder="ERP číslo pro meziprodukt"
                              />
                            ) : (
                              <span className="text-sm text-gray-900">
                                {order.erpOrderNumberSemiproduct || "-"}
                              </span>
                            )}
                          </div>
                          
                          <div className="flex items-center justify-between">
                            <div className="flex items-center">
                              <Hash className="h-4 w-4 text-gray-400 mr-2" />
                              <span className="text-sm text-gray-500">ERP č. (produkt):</span>
                            </div>
                            {canEditFields ? (
                              <input
                                type="text"
                                value={editableErpOrderNumberProduct}
                                onChange={(e) => setEditableErpOrderNumberProduct(e.target.value)}
                                className="w-48 text-sm border border-gray-300 rounded px-2 py-1"
                                placeholder="ERP číslo pro produkt"
                              />
                            ) : (
                              <span className="text-sm text-gray-900">
                                {order.erpOrderNumberProduct || "-"}
                              </span>
                            )}
                          </div>
                          
                          {/* Manual Action Required Section */}
                          <div className="flex items-center justify-between">
                            <div className="flex items-center">
                              <AlertCircle className="h-4 w-4 text-gray-400 mr-2" />
                              <span className="text-sm text-gray-500">Vyžaduje ruční zásah:</span>
                            </div>
                            <div className="flex items-center space-x-2">
                              <input
                                type="checkbox"
                                checked={order.manualActionRequired || false}
                                disabled={true}
                                className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500 disabled:opacity-50 disabled:cursor-not-allowed"
                              />
                              {order.manualActionRequired && (
                                <button
                                  onClick={() => setShowResolveModal(true)}
                                  className="px-3 py-1 text-xs bg-green-600 text-white rounded hover:bg-green-700 transition-colors duration-150"
                                >
                                  Vyřešeno
                                </button>
                              )}
                            </div>
                          </div>
                          
                          {/* Planned Dates Section */}
                       
                          <div className="space-y-2">
                            <div className="flex items-center justify-between">
                              <div className="flex items-center">
                                <Calendar className="h-4 w-4 text-gray-400 mr-2" />
                                <span className="text-sm text-gray-500">Datum:</span>
                              </div>
                              {canEditFields ? (
                                <input
                                  type="date"
                                  value={editableSemiProductDate}
                                  onChange={(e) => setEditableSemiProductDate(e.target.value)}
                                  className="text-sm border border-gray-300 rounded px-2 py-1"
                                />
                              ) : (
                                <span className="text-sm text-gray-900">
                                  {order.semiProductPlannedDate ? formatDate(order.semiProductPlannedDate) : "-"}
                                </span>
                              )}
                            </div>
                          </div>
                        <div className="mt-3 pt-3 border-t border-gray-200">
                          <div className="flex items-center justify-between">
                            <div className="flex items-center">
                              <Hash className="h-4 w-4 text-gray-400 mr-2" />
                              <span className="text-sm text-gray-500">Šarže:</span>
                            </div>
                            {canEditFields ? (
                              <input
                                type="text"
                                value={editableLotNumber}
                                onChange={(e) => setEditableLotNumber(e.target.value)}
                                className="text-sm border border-gray-300 rounded px-2 py-1 w-28"
                                placeholder="38202412"
                              />
                            ) : (
                              <span className="text-sm text-gray-900">
                                {order.semiProduct?.lotNumber || "-"}
                              </span>
                            )}
                          </div>
                          <div className="flex items-center justify-between">
                            <div className="flex items-center">
                              <CalendarClock className="h-4 w-4 text-gray-400 mr-2" />
                              <span className="text-sm text-gray-500">Expirace ({order.semiProduct?.expirationMonths} měsíců):</span>
                            </div>
                            {canEditFields ? (
                              <input
                                type="month"
                                lang="cs"
                                value={editableExpirationDate ? editableExpirationDate.substring(0, 7) : ""}
                                onChange={(e) => setEditableExpirationDate(e.target.value + "-01")}
                                className="text-sm border border-gray-300 rounded px-2 py-1"
                              />
                            ) : (
                              <span className="text-sm text-gray-900">
                                {order.semiProduct?.expirationDate ? formatDate(order.semiProduct.expirationDate) : "-"}
                              </span>
                            )}
                          </div>
                        </div>
                        </div>


                        

                        {/* Latest Note */}
                        <div className="mt-3 pt-3 border-t border-gray-200">
                          <h4 className="text-sm font-medium text-gray-700 mb-2">Poslední poznámka:</h4>
                          {order.notes && order.notes.length > 0 ? (
                            <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-3 relative">
                              <div className="flex items-start space-x-2">
                                <div className="p-1 bg-yellow-100 rounded-full mt-1">
                                  <StickyNote className="h-3 w-3 text-yellow-600" />
                                </div>
                                <div className="flex-1 min-w-0">
                                  <div className="flex items-center justify-between mb-1">
                                    <span className="text-sm font-medium text-gray-900">
                                      {order.notes[0].createdByUser || "Neznámý"}
                                    </span>
                                    <span className="text-xs text-gray-500">
                                      {formatDateTime(order.notes[0].createdAt)}
                                    </span>
                                  </div>
                                  <div className="relative">
                                    <p className="text-sm text-gray-800 whitespace-pre-wrap" style={{ 
                                      display: '-webkit-box', 
                                      WebkitLineClamp: 2, 
                                      WebkitBoxOrient: 'vertical', 
                                      overflow: 'hidden' 
                                    }}>
                                      {order.notes && order.notes[0]?.text && shouldTruncateText(order.notes[0].text) 
                                        ? truncateText(order.notes[0].text)
                                        : (order.notes && order.notes[0]?.text) || ''}
                                    </p>
                                    {order.notes && order.notes[0]?.text && shouldTruncateText(order.notes[0].text) && (
                                      <button
                                        onClick={() => handleExpandNote(order.notes![0].text || '')}
                                        className="absolute top-0 right-0 p-1 bg-yellow-100 hover:bg-yellow-200 rounded-full transition-colors"
                                        title="Rozbalit poznámku"
                                      >
                                        <Maximize2 className="h-3 w-3 text-yellow-600" />
                                      </button>
                                    )}
                                  </div>
                                </div>
                              </div>
                            </div>
                          ) : (
                            <p className="text-gray-500 text-sm italic">
                              Zatím nejsou žádné poznámky
                            </p>
                          )}
                        </div>

                      </div>

                      
                      </div>

                    {/* Right Panel - Semi-product and Products */}
                    <div className="flex flex-col space-y-4">
                      {/* Semi-product Highlighted Row */}
                      <div className="bg-blue-50 rounded-lg p-3">
                        {order.semiProduct ? (
                          <div className="flex items-center">
                            <div className="flex-1">
                              <div className="text-sm font-medium text-gray-900 mb-1">
                                {order.semiProduct.productName || "Bez názvu"}
                              </div>
                              <div className="text-sm text-gray-600">
                                {order.semiProduct.productCode || "Bez kódu"}
                              </div>
                            </div>
                            <div className="ml-2">
                              {canEditFields ? (
                                <div className="flex items-center">
                                  <input
                                    type="number"
                                    value={editableSemiProductQuantity}
                                    onChange={(e) => setEditableSemiProductQuantity(e.target.value)}
                                    className="text-lg font-bold text-gray-700 bg-white border border-gray-300 rounded px-2 py-1 w-25 text-center"
                                    min="0"
                                    step="1"
                                  />
                                  <span className="text-lg font-bold text-gray-900 ml-1">g</span>
                                </div>
                              ) : (
                                <div className="text-lg font-bold text-gray-900">
                                  {order.semiProduct.actualQuantity || order.semiProduct.plannedQuantity || "0"}g
                                  {order.semiProduct.actualQuantity && order.semiProduct.plannedQuantity && order.semiProduct.actualQuantity !== order.semiProduct.plannedQuantity && (
                                    <span className="text-xs text-gray-500 ml-1">
                                      (plán: {order.semiProduct.plannedQuantity}g)
                                    </span>
                                  )}
                                </div>
                              )}
                            </div>
                          </div>
                        ) : (
                          <p className="text-gray-500 text-center text-sm italic">
                            Polotovar není definován
                          </p>
                        )}
                      </div>

                      {/* Products Datagrid */}
                        {order.products && order.products.length > 0 ? (
                          <div className="bg-white rounded-lg overflow-hidden border border-green-200">
                            <table className="min-w-full divide-y divide-gray-200">
                              <thead className="bg-green-100">
                                <tr>
                                  <th className="px-4 py-3 text-left text-xs font-medium text-green-800 uppercase tracking-wider">
                                    Kód
                                  </th>
                                  <th className="px-4 py-3 text-left text-xs font-medium text-green-800 uppercase tracking-wider">
                                    Název
                                  </th>
                                  <th className="px-4 py-3 text-left text-xs font-medium text-green-800 uppercase tracking-wider">
                                    Množství
                                  </th>
                                </tr>
                              </thead>
                              <tbody className="bg-white divide-y divide-gray-200">
                                {order.products.map((product, index) => (
                                  <tr key={index} className="hover:bg-gray-50">
                                    <td className="px-4 py-3 text-sm font-medium text-gray-900">
                                      {product.productCode || "-"}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-700">
                                      {product.productName || "-"}
                                    </td>
                                    <td className="px-4 py-3 text-sm text-gray-700">
                                      {canEditFields ? (
                                        <input
                                          type="number"
                                          value={editableProductQuantities[index] || ""}
                                          onChange={(e) => handleProductQuantityChange(index, e.target.value)}
                                          className="w-20 px-2 py-1 border border-gray-300 rounded text-center"
                                          min="0"
                                          step="1"
                                        />
                                      ) : (
                                        <div>
                                          {product.actualQuantity || product.plannedQuantity || "-"}
                                          {product.actualQuantity && product.plannedQuantity && product.actualQuantity !== product.plannedQuantity && (
                                            <span className="text-xs text-gray-500 ml-1">
                                              (plán: {product.plannedQuantity})
                                            </span>
                                          )}
                                        </div>
                                      )}
                                    </td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        ) : (
                          <div className="bg-white rounded-lg p-6 border border-green-200">
                            <p className="text-gray-500 text-center text-sm italic">
                              Žádné produkty nejsou definovány
                            </p>
                          </div>
                        )}
                    </div>
                  </div>
                )}

                {activeTab === "notes" && (
                  <div>
                    <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                      <StickyNote className="h-5 w-5 mr-2 text-indigo-600" />
                      Poznámky
                    </h3>
                    
                    {/* Add New Note */}
                    <div className="mb-6 bg-gray-50 rounded-lg p-4">
                      <label htmlFor="newNote" className="block text-sm font-medium text-gray-700 mb-2">
                        Přidat poznámku:
                      </label>
                      <textarea
                        id="newNote"
                        value={newNote}
                        onChange={(e) => setNewNote(e.target.value)}
                        placeholder="Napište poznámku..."
                        className="w-full h-20 px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 resize-none text-sm"
                        rows={3}
                      />
                    </div>

                    {order.notes && order.notes.length > 0 ? (
                      <div className="space-y-4">
                        {order.notes.map((note, index) => (
                          <div key={index} className="bg-yellow-50 border border-yellow-200 rounded-lg p-4">
                            <div className="flex items-start space-x-3">
                              <StickyNote className="h-5 w-5 text-yellow-600 mt-0.5" />
                              <div className="flex-1">
                                <div className="flex items-center justify-between mb-2">
                                  <span className="text-sm font-medium text-gray-900">
                                    {note.createdByUser || "Neznámý"}
                                  </span>
                                  <span className="text-xs text-gray-500">
                                    {formatDateTime(note.createdAt)}
                                  </span>
                                </div>
                                <p className="text-sm text-gray-900 whitespace-pre-wrap">
                                  {note.text}
                                </p>
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="text-gray-500 text-center py-8">Žádné poznámky nebyly přidány.</p>
                    )}
                  </div>
                )}

                {activeTab === "log" && (
                  <div>
                    <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                      <ScrollText className="h-5 w-5 mr-2 text-indigo-600" />
                      Audit log
                    </h3>
                    {order.auditLog && order.auditLog.length > 0 ? (
                      <div className="bg-white shadow rounded-lg overflow-hidden">
                        <table className="min-w-full divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Čas
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Akce
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Uživatel
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Detaily
                              </th>
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {order.auditLog.map((logEntry, index) => (
                              <tr key={index}>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  {formatDateTime(logEntry.timestamp)}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {getAuditActionLabel(logEntry.action)}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {logEntry.user || "-"}
                                </td>
                                <td className="px-6 py-4 text-sm text-gray-500">
                                  {logEntry.details || "-"}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <p className="text-gray-500 text-center py-8">Žádné záznamy v audit logu.</p>
                    )}
                  </div>
                )}
              </div>
            </>
          ) : null}
        </div>

        {/* Separator and Action Buttons */}
        <div className="border-t border-gray-200 p-3 flex-shrink-0">
          <div className="flex items-center justify-between">
            {/* Cancel button on the left */}
            <div>
              {order && order.state !== ManufactureOrderState.Completed && order.state !== ManufactureOrderState.Cancelled && (
                <button
                  onClick={() => setShowCancelConfirmation(true)}
                  disabled={updateOrderStatusMutation.isPending}
                  className="flex items-center px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
                  title="Stornovat zakázku"
                >
                  <Ban className="h-4 w-4 mr-1" />
                  Stornovat
                </button>
              )}
            </div>

            {/* Duplicate button in the center */}
            <div>
              {order && (
                <button
                  onClick={handleDuplicate}
                  disabled={duplicateOrderMutation.isPending}
                  className="flex items-center px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
                  title="Duplikovat zakázku"
                >
                  {duplicateOrderMutation.isPending ? (
                    <Loader2 className="h-4 w-4 mr-1 animate-spin" />
                  ) : (
                    <Copy className="h-4 w-4 mr-1" />
                  )}
                  Duplikovat
                </button>
              )}
            </div>
            
            {/* Close and Save buttons on the right */}
            <div className="flex items-center space-x-2">
            <button
              onClick={handleCloseWithWeekNavigation}
              className="flex items-center px-4 py-2 bg-gray-500 text-white rounded-lg hover:bg-gray-600 transition-colors text-sm"
            >
              <XCircle className="h-4 w-4 mr-1" />
              Close
            </button>
            <button
              onClick={handleSave}
              disabled={updateOrderMutation.isPending}
              className="flex items-center px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors text-sm disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {updateOrderMutation.isPending ? (
                <Loader2 className="h-4 w-4 mr-1 animate-spin" />
              ) : (
                <Save className="h-4 w-4 mr-1" />
              )}
              Save
            </button>
            </div>
          </div>
        </div>

        {/* Cancel Confirmation Dialog */}
        {showCancelConfirmation && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-[60]">
            <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
              <div className="p-6">
                <div className="flex items-center mb-4">
                  <AlertCircle className="h-6 w-6 text-red-600 mr-3" />
                  <h3 className="text-lg font-semibold text-gray-900">
                    Stornovat zakázku
                  </h3>
                </div>
                <p className="text-gray-600 mb-6">
                  Opravdu chcete stornovat tuto výrobní zakázku? Tato akce je nevratná.
                </p>
                <div className="flex justify-end space-x-3">
                  <button
                    onClick={() => setShowCancelConfirmation(false)}
                    className="px-4 py-2 text-gray-700 bg-gray-100 rounded-lg hover:bg-gray-200 transition-colors"
                  >
                    Zrušit
                  </button>
                  <button
                    onClick={async () => {
                      setShowCancelConfirmation(false);
                      await handleStateChange(ManufactureOrderState.Cancelled);
                    }}
                    disabled={updateOrderStatusMutation.isPending}
                    className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {updateOrderStatusMutation.isPending ? (
                      <>
                        <Loader2 className="h-4 w-4 mr-1 animate-spin inline" />
                        Stornuji...
                      </>
                    ) : (
                      'Stornovat zakázku'
                    )}
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* Confirm Semi-Product Quantity Modal */}
        {showQuantityConfirmModal && order?.semiProduct && (
          <ConfirmSemiProductQuantityModal
            isOpen={showQuantityConfirmModal}
            onClose={() => setShowQuantityConfirmModal(false)}
            onSubmit={handleConfirmQuantity}
            orderId={orderId}
            plannedQuantity={order.semiProduct.plannedQuantity || 0}
            productName={order.semiProduct.productName || ""}
            isLoading={confirmSemiProductMutation.isPending}
          />
        )}

        {/* Confirm Product Completion Modal */}
        {showProductCompletionModal && order?.products && order.products.length > 0 && (
          <ConfirmProductCompletionModal
            isOpen={showProductCompletionModal}
            onClose={() => setShowProductCompletionModal(false)}
            onSubmit={handleConfirmProductCompletion}
            orderId={orderId}
            products={order.products.map(product => ({
              id: product.id || 0,
              productCode: product.productCode || "",
              productName: product.productName || "",
              plannedQuantity: product.plannedQuantity || 0
            }))}
            isLoading={confirmProductCompletionMutation.isPending}
          />
        )}

        {/* Resolve Manual Action Modal */}
        {showResolveModal && order && (
          <ResolveManualActionModal
            isOpen={showResolveModal}
            onClose={() => setShowResolveModal(false)}
            orderId={orderId!}
            currentErpSemiproduct={order.erpOrderNumberSemiproduct || ""}
            currentErpProduct={order.erpOrderNumberProduct || ""}
            onSuccess={() => {
              setShowResolveModal(false);
              // Refresh the order data
              queryClient.invalidateQueries({
                queryKey: ["manufacture-order", orderId]
              });
              queryClient.invalidateQueries({
                queryKey: ["manufacture-orders"]
              });
              queryClient.invalidateQueries({
                queryKey: ["manufactureOrders", "calendar"]
              });
            }}
          />
        )}

        {/* Expanded Note Modal */}
        {showExpandedNote && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-[70]">
            <div className="bg-white rounded-lg shadow-xl max-w-2xl w-full max-h-[80vh] overflow-hidden">
              <div className="p-6">
                <div className="flex items-center justify-between mb-4">
                  <h3 className="text-lg font-semibold text-gray-900 flex items-center">
                    <StickyNote className="h-5 w-5 text-yellow-600 mr-2" />
                    Poznámka
                  </h3>
                  <button
                    onClick={() => setShowExpandedNote(false)}
                    className="text-gray-400 hover:text-gray-600 transition-colors"
                  >
                    <X className="h-6 w-6" />
                  </button>
                </div>
                <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4 max-h-[60vh] overflow-y-auto">
                  <p className="text-gray-800 whitespace-pre-wrap text-sm leading-relaxed">
                    {expandedNoteContent}
                  </p>
                </div>
                <div className="mt-4 flex justify-end">
                  <button
                    onClick={() => setShowExpandedNote(false)}
                    className="px-4 py-2 bg-gray-100 text-gray-700 rounded-lg hover:bg-gray-200 transition-colors"
                  >
                    Zavřít
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
    </div>
  );

  // Return content wrapped appropriately for modal or page mode
  if (isModalMode) {
    return (
      <div
        className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
        onClick={handleBackdropClick}
      >
        {content}
      </div>
    );
  } else {
    return (
      <div className="h-screen bg-gray-50 overflow-hidden">
        <div className="max-w-6xl mx-auto py-4 px-4 sm:px-6 lg:px-8 h-full">
          {content}
        </div>
      </div>
    );
  }
};

export default ManufactureOrderDetail;