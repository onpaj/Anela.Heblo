import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { useParams, useNavigate, Navigate } from "react-router-dom";
import {
  Loader2,
  AlertCircle,
  Edit,
  Info,
  ScrollText,
  StickyNote,
  Factory,
  ArrowLeft,
  X,
} from "lucide-react";
import {
  useManufactureOrderDetailQuery,
  useUpdateManufactureOrder,
  useUpdateManufactureOrderStatus,
  useConfirmSemiProductManufacture,
  useConfirmProductCompletion,
  useDuplicateManufactureOrder,
} from "../../../api/hooks/useManufactureOrders";
import { useQueryClient } from "@tanstack/react-query";
import {
  UpdateManufactureOrderRequest,
  UpdateManufactureOrderStatusRequest,
  UpdateManufactureOrderProductRequest,
  UpdateManufactureOrderSemiProductRequest,
  ConfirmSemiProductManufactureRequest,
  ConfirmProductCompletionRequest,
  ManufactureOrderState,
} from "../../../api/generated/api-client";

// Import our extracted components
import StateTransitionControls from "../detail/StateTransitionControls";
import BasicInfoSection from "../detail/BasicInfoSection";
import SemiProductSection from "../detail/SemiProductSection";
import ProductsDataGrid from "../detail/ProductsDataGrid";
import NotesTabContent from "../detail/NotesTabContent";
import AuditLogTabContent from "../detail/AuditLogTabContent";
import DetailActionButtons from "../detail/DetailActionButtons";
import ConfirmationDialogs from "../detail/ConfirmationDialogs";

interface ManufactureOrderDetailProps {
  orderId?: number;
  isOpen?: boolean;
  onClose?: () => void;
  onEdit?: (orderId: number) => void;
}

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
  
  // Determine if this is modal mode or page mode
  const isModalMode = propOrderId !== undefined;
  const urlOrderId = id ? parseInt(id, 10) : 0;
  const orderId = isModalMode ? propOrderId : urlOrderId;
  
  // Tab state
  const [activeTab, setActiveTab] = useState<"info" | "notes" | "log">("info");
  
  // Note input state
  const [newNote, setNewNote] = useState("");

  // Editable fields state
  const [editableResponsiblePerson, setEditableResponsiblePerson] = useState("");
  const [editableSemiProductDate, setEditableSemiProductDate] = useState("");
  const [editableProductDate, setEditableProductDate] = useState("");
  const [editableSemiProductQuantity, setEditableSemiProductQuantity] = useState("");
  const [editableProductQuantities, setEditableProductQuantities] = useState<Record<number, string>>({});
  const [editableLotNumber, setEditableLotNumber] = useState("");
  const [editableExpirationDate, setEditableExpirationDate] = useState("");
  const [editableErpOrderNumberSemiproduct, setEditableErpOrderNumberSemiproduct] = useState("");
  const [editableErpOrderNumberProduct, setEditableErpOrderNumberProduct] = useState("");
  const [editableErpDiscardResidueDocumentNumber, setEditableErpDiscardResidueDocumentNumber] = useState("");
  const [editableManualActionRequired, setEditableManualActionRequired] = useState(false);
  
  // Confirmation dialog state
  const [showCancelConfirmation, setShowCancelConfirmation] = useState(false);
  const [showStateReturnConfirmation, setShowStateReturnConfirmation] = useState(false);
  const [pendingStateChange, setPendingStateChange] = useState<ManufactureOrderState | null>(null);
  const [showQuantityConfirmModal, setShowQuantityConfirmModal] = useState(false);
  const [showProductCompletionModal, setShowProductCompletionModal] = useState(false);
  const [showResolveModal, setShowResolveModal] = useState(false);
  const [showExpandedNote, setShowExpandedNote] = useState(false);
  const [expandedNoteContent, setExpandedNoteContent] = useState("");

  // Fetch order details
  const {
    data: orderData,
    isLoading: orderLoading,
    error: orderError,
  } = useManufactureOrderDetailQuery(orderId || 0);

  const order = orderData?.order;

  // Mutations
  const updateOrderMutation = useUpdateManufactureOrder();
  const updateOrderStatusMutation = useUpdateManufactureOrderStatus();
  const confirmSemiProductMutation = useConfirmSemiProductManufacture();
  const confirmProductCompletionMutation = useConfirmProductCompletion();
  const duplicateOrderMutation = useDuplicateManufactureOrder();

  // Helper functions
  const getStateLabel = (state: ManufactureOrderState): string => {
    const stateKey = state.toString();
    const translationKey = `manufacture.states.${stateKey}`;
    const translated = t(translationKey);
    return translated || stateKey;
  };

  const getOrderPlannedDate = (order: any): Date => {
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

  const getAuditActionLabel = (action: any): string => {
    const actionName = typeof action === 'string' ? action : action?.toString();
    return t(`manufacture.auditActions.${actionName}`) || actionName || '-';
  };

  const shouldTruncateText = (text: string): boolean => text.length > 100;
  const truncateText = (text: string): string => text.length <= 100 ? text : text.substring(0, 97) + '...';

  const formatDateTime = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleString("cs-CZ");
  };

  const formatDate = (date: Date | string | undefined) => {
    if (!date) return "-";
    const dateObj = typeof date === "string" ? new Date(date) : date;
    return dateObj.toLocaleDateString("cs-CZ");
  };

  // Handler functions
  const handleClose = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["manufacture-orders"] });
    queryClient.invalidateQueries({ queryKey: ["manufactureOrders", "calendar"] });

    if (onClose) {
      onClose();
    } else {
      navigate("/manufacturing/orders");
    }
  }, [onClose, navigate, queryClient]);

  const handleCloseWithWeekNavigation = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["manufacture-orders"] });
    queryClient.invalidateQueries({ queryKey: ["manufactureOrders", "calendar"] });

    if (onClose) {
      onClose();
    } else {
      if (order) {
        const updatedSemiProductDate = editableSemiProductDate ? new Date(editableSemiProductDate) : null;
        const updatedProductDate = editableProductDate ? new Date(editableProductDate) : null;
        const orderDate = updatedSemiProductDate || updatedProductDate || getOrderPlannedDate(order);
        const dateString = orderDate.toISOString().split('T')[0];
        navigate(`/manufacturing/orders?view=weekly&date=${dateString}`);
      } else {
        navigate("/manufacturing/orders");
      }
    }
  }, [onClose, navigate, queryClient, order, editableSemiProductDate, editableProductDate]);

  const handleExpandNote = (noteText: string) => {
    setExpandedNoteContent(noteText);
    setShowExpandedNote(true);
  };

  // State transitions logic
  const getStateTransitions = (currentState: ManufactureOrderState) => {
    const transitions = {
      [ManufactureOrderState.Draft]: { next: ManufactureOrderState.Planned, previous: null },
      [ManufactureOrderState.Planned]: { next: ManufactureOrderState.SemiProductManufactured, previous: ManufactureOrderState.Draft },
      [ManufactureOrderState.SemiProductManufactured]: { next: ManufactureOrderState.Completed, previous: ManufactureOrderState.Planned },
      [ManufactureOrderState.Completed]: { next: null, previous: ManufactureOrderState.SemiProductManufactured },
      [ManufactureOrderState.Cancelled]: { next: null, previous: null },
    };
    return transitions[currentState] || { next: null, previous: null };
  };

  const isStateTransitionBackward = (currentState: ManufactureOrderState, newState: ManufactureOrderState): boolean => {
    const stateOrder = {
      [ManufactureOrderState.Draft]: 0,
      [ManufactureOrderState.Planned]: 1,
      [ManufactureOrderState.SemiProductManufactured]: 2,
      [ManufactureOrderState.Completed]: 3,
      [ManufactureOrderState.Cancelled]: 4,
    };
    return stateOrder[newState] < stateOrder[currentState];
  };

  const handleStateChange = async (newState: ManufactureOrderState) => {
    if (!order || !orderId || order.state === undefined) return;

    const isBackwardTransition = isStateTransitionBackward(order.state, newState);
    const isReturnToDraft = newState === ManufactureOrderState.Draft;

    if (isBackwardTransition && !isReturnToDraft) {
      setPendingStateChange(newState);
      setShowStateReturnConfirmation(true);
      return;
    }

    await executeStateChange(newState);
  };

  const executeStateChange = async (newState: ManufactureOrderState) => {
    if (!order || !orderId) return;

    if (order.state === ManufactureOrderState.Planned && newState === ManufactureOrderState.SemiProductManufactured) {
      setShowQuantityConfirmModal(true);
      return;
    }

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
    }
  };

  // Auto-calculate lot number and expiration date
  const getWeekNumber = (date: Date): number => {
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const dayNum = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    return Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
  };

  // Initialize editable fields when order data changes
  React.useEffect(() => {
    if (order) {
      const fieldsCanBeEdited = order.state === ManufactureOrderState.Draft || order.state === ManufactureOrderState.Planned;
      
      setEditableResponsiblePerson(order.responsiblePerson || "");
      setEditableSemiProductDate(order.semiProductPlannedDate ? new Date(order.semiProductPlannedDate).toISOString().split('T')[0] : "");
      setEditableProductDate(order.productPlannedDate ? new Date(order.productPlannedDate).toISOString().split('T')[0] : "");
      
      const semiProductQuantity = fieldsCanBeEdited 
        ? order.semiProduct?.plannedQuantity?.toString() || ""
        : order.semiProduct?.actualQuantity?.toString() || order.semiProduct?.plannedQuantity?.toString() || "";
      setEditableSemiProductQuantity(semiProductQuantity);
      
      setEditableLotNumber(order.semiProduct?.lotNumber || "");
      setEditableExpirationDate(order.semiProduct?.expirationDate ? new Date(order.semiProduct.expirationDate).toISOString().split('T')[0] : "");
      setEditableErpOrderNumberSemiproduct(order.erpOrderNumberSemiproduct || "");
      setEditableErpOrderNumberProduct(order.erpOrderNumberProduct || "");
      setEditableErpDiscardResidueDocumentNumber(order.erpDiscardResidueDocumentNumber || "");
      setEditableManualActionRequired(order.manualActionRequired || false);
      
      const productQuantities: Record<number, string> = {};
      order.products?.forEach((product: any, index: number) => {
        const quantity = fieldsCanBeEdited 
          ? product.plannedQuantity?.toString() || ""
          : product.actualQuantity?.toString() || product.plannedQuantity?.toString() || "";
        productQuantities[index] = quantity;
      });
      setEditableProductQuantities(productQuantities);
    }
  }, [order]);

  // Auto-calculate lot number and expiration date when semi-product planned date changes
  React.useEffect(() => {
    if (editableSemiProductDate) {
      const semiProductDate = new Date(editableSemiProductDate);
      
      const year = semiProductDate.getFullYear();
      const month = String(semiProductDate.getMonth() + 1).padStart(2, '0');
      const week = String(getWeekNumber(semiProductDate)).padStart(2, '0');
      const newLotNumber = `${week}${year}${month}`;
      setEditableLotNumber(newLotNumber);
      
      const expirationMonths = order?.semiProduct?.expirationMonths || 12;
      const expirationDate = new Date(semiProductDate);
      expirationDate.setMonth(expirationDate.getMonth() + expirationMonths);
      const lastDayOfExpirationMonth = new Date(expirationDate.getFullYear(), expirationDate.getMonth() + 1, 0).getDate();
      expirationDate.setDate(lastDayOfExpirationMonth);
      const newExpirationDateString = expirationDate.toISOString().split('T')[0];
      setEditableExpirationDate(newExpirationDateString);
    }
  }, [editableSemiProductDate, order?.semiProduct?.expirationMonths]);

  // Keyboard event listener for Esc key
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

  // Save handler
  const handleSave = async () => {
    if (!order || !orderId) return;

    try {
      const canEditFields = order?.state === ManufactureOrderState.Draft || order?.state === ManufactureOrderState.Planned;
      
      const products = order.products?.map((product: any, index: number) => new UpdateManufactureOrderProductRequest({
        id: product.id,
        productCode: product.productCode || "",
        productName: product.productName || "",
        plannedQuantity: canEditFields ? (parseFloat(editableProductQuantities[index] || "0") || 0) : undefined,
      })) || [];

      const semiProductRequest = (editableLotNumber || editableExpirationDate || (canEditFields && editableSemiProductQuantity)) ? new UpdateManufactureOrderSemiProductRequest({
        plannedQuantity: canEditFields && editableSemiProductQuantity ? parseFloat(editableSemiProductQuantity) || undefined : undefined,
        lotNumber: editableLotNumber || undefined,
        expirationDate: editableExpirationDate ? (() => {
          const [year, month] = editableExpirationDate.split('-').map(Number);
          return new Date(year, month, 0);
        })() : undefined,
      }) : undefined;

      const request = new UpdateManufactureOrderRequest({
        id: orderId,
        semiProductPlannedDate: editableSemiProductDate ? new Date(editableSemiProductDate) : (order.semiProductPlannedDate ? new Date(order.semiProductPlannedDate) : new Date()),
        productPlannedDate: editableProductDate ? new Date(editableProductDate) : (order.productPlannedDate ? new Date(order.productPlannedDate) : new Date()),
        responsiblePerson: editableResponsiblePerson || undefined,
        erpOrderNumberSemiproduct: editableErpOrderNumberSemiproduct || undefined,
        erpOrderNumberProduct: editableErpOrderNumberProduct || undefined,
        erpDiscardResidueDocumentNumber: editableErpDiscardResidueDocumentNumber || undefined,
        semiProduct: semiProductRequest,
        products,
        newNote: newNote.trim() || undefined,
        manualActionRequired: editableManualActionRequired,
      });
      
      await updateOrderMutation.mutateAsync(request);

      if (newNote.trim()) {
        setNewNote("");
      }
      
      if (onClose) {
        onClose();
      } else {
        const updatedSemiProductDate = editableSemiProductDate ? new Date(editableSemiProductDate) : null;
        const updatedProductDate = editableProductDate ? new Date(editableProductDate) : null;
        const orderDate = updatedSemiProductDate || updatedProductDate || getOrderPlannedDate(order);
        const dateString = orderDate.toISOString().split('T')[0];
        navigate(`/manufacturing/orders?view=weekly&date=${dateString}`);
      }
    } catch (error) {
      console.error("Error updating order:", error);
    }
  };

  const handleProductQuantityChange = (index: number, value: string) => {
    setEditableProductQuantities(prev => ({
      ...prev,
      [index]: value
    }));
  };

  const handleDuplicate = async () => {
    if (!orderId) return;

    try {
      const result = await duplicateOrderMutation.mutateAsync(orderId);
      
      if (result.id) {
        const newOrderUrl = `/manufacturing/orders/${result.id}`;
        
        if (onEdit && onClose) {
          onEdit(result.id);
        } else {
          navigate(newOrderUrl);
        }
      }
    } catch (error) {
      console.error("Error duplicating order:", error);
    }
  };

  const handleConfirmQuantity = async (request: ConfirmSemiProductManufactureRequest) => {
    try {
      await confirmSemiProductMutation.mutateAsync(request);
      setShowQuantityConfirmModal(false);
      handleCloseWithWeekNavigation();
    } catch (error) {
      console.error("Error confirming semi-product quantity:", error);
      throw error;
    }
  };

  const handleConfirmProductCompletion = async (request: ConfirmProductCompletionRequest) => {
    try {
      await confirmProductCompletionMutation.mutateAsync(request);
      setShowProductCompletionModal(false);
      handleCloseWithWeekNavigation();
    } catch (error) {
      console.error("Error confirming product completion:", error);
      throw error;
    }
  };

  // Validation
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
              {order ? `Výrobní zakázka ${order.orderNumber}` : "Načítání..."}
            </h2>
            <p className="text-sm text-gray-500">ID: {orderId}</p>
          </div>
        </div>
        <div className="flex items-center space-x-2">
          <StateTransitionControls
            order={order}
            currentStateTransitions={currentStateTransitions}
            onStateChange={handleStateChange}
            isLoading={updateOrderStatusMutation.isPending}
            getStateLabel={getStateLabel}
          />
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
            {isModalMode ? <X className="h-6 w-6" /> : <ArrowLeft className="h-6 w-6" />}
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-hidden flex flex-col min-h-0">
        {orderLoading ? (
          <div className="flex items-center justify-center h-64">
            <div className="flex items-center space-x-2">
              <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
              <div className="text-gray-500">Načítání detailů zakázky...</div>
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
                  <div className="flex flex-col space-y-4">
                    <BasicInfoSection
                      order={order}
                      canEditFields={canEditFields}
                      editableResponsiblePerson={editableResponsiblePerson}
                      editableErpOrderNumberSemiproduct={editableErpOrderNumberSemiproduct}
                      editableErpOrderNumberProduct={editableErpOrderNumberProduct}
                      editableErpDiscardResidueDocumentNumber={editableErpDiscardResidueDocumentNumber}
                      editableSemiProductDate={editableSemiProductDate}
                      editableLotNumber={editableLotNumber}
                      editableExpirationDate={editableExpirationDate}
                      editableManualActionRequired={editableManualActionRequired}
                      onResponsiblePersonChange={(value) => setEditableResponsiblePerson(value || "")}
                      onErpOrderNumberSemiproductChange={setEditableErpOrderNumberSemiproduct}
                      onErpOrderNumberProductChange={setEditableErpOrderNumberProduct}
                      onErpDiscardResidueDocumentNumberChange={setEditableErpDiscardResidueDocumentNumber}
                      onSemiProductDateChange={setEditableSemiProductDate}
                      onLotNumberChange={setEditableLotNumber}
                      onExpirationDateChange={setEditableExpirationDate}
                      onManualActionRequiredChange={setEditableManualActionRequired}
                      onResolveManualAction={() => setShowResolveModal(true)}
                      onExpandNote={handleExpandNote}
                      formatDateTime={formatDateTime}
                      formatDate={formatDate}
                      shouldTruncateText={shouldTruncateText}
                      truncateText={truncateText}
                    />
                  </div>

                  <div className="flex flex-col space-y-4">
                    <SemiProductSection
                      order={order}
                      canEditFields={canEditFields}
                      editableSemiProductQuantity={editableSemiProductQuantity}
                      onSemiProductQuantityChange={setEditableSemiProductQuantity}
                    />
                    
                    <ProductsDataGrid
                      order={order}
                      canEditFields={canEditFields}
                      editableProductQuantities={editableProductQuantities}
                      onProductQuantityChange={handleProductQuantityChange}
                    />
                  </div>
                </div>
              )}

              {activeTab === "notes" && (
                <NotesTabContent
                  order={order}
                  newNote={newNote}
                  onNewNoteChange={setNewNote}
                  formatDateTime={formatDateTime}
                />
              )}

              {activeTab === "log" && (
                <AuditLogTabContent
                  order={order}
                  formatDateTime={formatDateTime}
                  getAuditActionLabel={getAuditActionLabel}
                />
              )}
            </div>
          </>
        ) : null}
      </div>

      <DetailActionButtons
        order={order}
        onCancel={() => setShowCancelConfirmation(true)}
        onDuplicate={handleDuplicate}
        onClose={handleCloseWithWeekNavigation}
        onSave={handleSave}
        isUpdateLoading={updateOrderMutation.isPending}
        isDuplicateLoading={duplicateOrderMutation.isPending}
      />

      <ConfirmationDialogs
        showCancelConfirmation={showCancelConfirmation}
        onCancelConfirmationClose={() => setShowCancelConfirmation(false)}
        onCancelConfirm={async () => {
          setShowCancelConfirmation(false);
          await handleStateChange(ManufactureOrderState.Cancelled);
        }}
        showStateReturnConfirmation={showStateReturnConfirmation}
        pendingStateChange={pendingStateChange}
        onStateReturnConfirmationClose={() => {
          setShowStateReturnConfirmation(false);
          setPendingStateChange(null);
        }}
        onStateReturnConfirm={async () => {
          setShowStateReturnConfirmation(false);
          const stateToChange = pendingStateChange;
          setPendingStateChange(null);
          if (stateToChange) {
            await executeStateChange(stateToChange);
          }
        }}
        showQuantityConfirmModal={showQuantityConfirmModal}
        onQuantityConfirmModalClose={() => setShowQuantityConfirmModal(false)}
        onQuantityConfirm={handleConfirmQuantity}
        showProductCompletionModal={showProductCompletionModal}
        onProductCompletionModalClose={() => setShowProductCompletionModal(false)}
        onProductCompletionConfirm={handleConfirmProductCompletion}
        showResolveModal={showResolveModal}
        onResolveModalClose={() => setShowResolveModal(false)}
        onResolveSuccess={() => {
          setShowResolveModal(false);
          queryClient.invalidateQueries({ queryKey: ["manufacture-order", orderId] });
          queryClient.invalidateQueries({ queryKey: ["manufacture-orders"] });
          queryClient.invalidateQueries({ queryKey: ["manufactureOrders", "calendar"] });
        }}
        showExpandedNote={showExpandedNote}
        expandedNoteContent={expandedNoteContent}
        onExpandedNoteClose={() => setShowExpandedNote(false)}
        order={order}
        orderId={orderId}
        isUpdateLoading={updateOrderStatusMutation.isPending}
        isQuantityLoading={confirmSemiProductMutation.isPending}
        isProductCompletionLoading={confirmProductCompletionMutation.isPending}
        getStateLabel={getStateLabel}
      />
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