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
} from "lucide-react";
import {
  useManufactureOrderDetailQuery,
  useUpdateManufactureOrder,
  useUpdateManufactureOrderStatus,
  ManufactureOrderState,
} from "../../api/hooks/useManufactureOrders";
import { useQueryClient } from "@tanstack/react-query";
import {
  UpdateManufactureOrderRequest,
  UpdateManufactureOrderStatusRequest,
  UpdateManufactureOrderProductRequest,
  UpdateManufactureOrderSemiProductRequest,
} from "../../api/generated/api-client";

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
  
  // Helper function to get translated audit action label
  const getAuditActionLabel = (action: any): string => {
    const actionName = typeof action === 'string' ? action : action?.toString();
    return t(`manufacture.auditActions.${actionName}`) || actionName || '-';
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
  // Confirmation dialog state
  const [showCancelConfirmation, setShowCancelConfirmation] = useState(false);

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

  // Initialize editable fields when order data changes
  React.useEffect(() => {
    if (order) {
      setEditableResponsiblePerson(order.responsiblePerson || "");
      setEditableSemiProductDate(order.semiProductPlannedDate ? new Date(order.semiProductPlannedDate).toISOString().split('T')[0] : "");
      setEditableProductDate(order.productPlannedDate ? new Date(order.productPlannedDate).toISOString().split('T')[0] : "");
      setEditableSemiProductQuantity(order.semiProduct?.plannedQuantity?.toString() || "");
      
      // Initialize lot number and expiration date
      setEditableLotNumber(order.semiProduct?.lotNumber || "");
      setEditableExpirationDate(order.semiProduct?.expirationDate ? new Date(order.semiProduct.expirationDate).toISOString().split('T')[0] : "");
      
      // Initialize product quantities
      const productQuantities: Record<number, string> = {};
      order.products?.forEach((product, index) => {
        productQuantities[index] = product.plannedQuantity?.toString() || "";
      });
      setEditableProductQuantities(productQuantities);
    }
  }, [order]);

  // Auto-calculate lot number and expiration date when semi-product planned date changes
  React.useEffect(() => {
    if (editableSemiProductDate) {
      const semiProductDate = new Date(editableSemiProductDate);
      
      // Auto-set lot number as "yyyyMM" format
      const year = semiProductDate.getFullYear();
      const month = String(semiProductDate.getMonth() + 1).padStart(2, '0');
      const newLotNumber = `${year}${month}`;
      
      // Only update if it's empty or was previously auto-calculated
      if (!editableLotNumber || /^\d{6}$/.test(editableLotNumber)) {
        setEditableLotNumber(newLotNumber);
      }
      
      // Auto-set expiration date as last day of the month + 1 year
      const expirationYear = year + 1;
      const expirationMonth = semiProductDate.getMonth(); // 0-indexed
      const lastDayOfMonth = new Date(expirationYear, expirationMonth + 1, 0).getDate();
      const expirationDate = new Date(expirationYear, expirationMonth, lastDayOfMonth);
      const newExpirationDateString = expirationDate.toISOString().split('T')[0];
      
      // Only update if it's empty or was previously auto-calculated
      if (!editableExpirationDate || editableExpirationDate.includes(`${expirationYear}`)) {
        setEditableExpirationDate(newExpirationDateString);
      }
    }
  }, [editableSemiProductDate, editableLotNumber, editableExpirationDate]);

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
      // Prepare products data
      const products = order.products?.map((product, index) => new UpdateManufactureOrderProductRequest({
        productCode: product.productCode || "",
        productName: product.productName || "",
        plannedQuantity: parseFloat(editableProductQuantities[index] || "0") || 0,
      })) || [];

      const request = new UpdateManufactureOrderRequest({
        id: orderId,
        semiProductPlannedDate: editableSemiProductDate ? new Date(editableSemiProductDate) : (order.semiProductPlannedDate ? new Date(order.semiProductPlannedDate) : new Date()),
        productPlannedDate: editableProductDate ? new Date(editableProductDate) : (order.productPlannedDate ? new Date(order.productPlannedDate) : new Date()),
        responsiblePerson: editableResponsiblePerson || undefined,
        semiProduct: editableLotNumber || editableExpirationDate ? new UpdateManufactureOrderSemiProductRequest({
          lotNumber: editableLotNumber || undefined,
          expirationDate: editableExpirationDate ? new Date(editableExpirationDate) : undefined,
        }) : undefined,
        products,
        newNote: newNote.trim() || undefined,
      });
      await updateOrderMutation.mutateAsync(request);

      // Clear the note input after saving
      if (newNote.trim()) {
        setNewNote("");
      }
      
      // Close the card after successful save
      handleClose();
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

  // Check if fields can be edited based on state
  const canEditFields = order?.state !== ManufactureOrderState.Completed && order?.state !== ManufactureOrderState.Cancelled;

  const currentStateTransitions = order?.state !== undefined ? getStateTransitions(order.state) : { next: null, previous: null };

  const content = (
    <div className={`bg-white ${isModalMode ? 'rounded-lg shadow-xl' : ''} ${isModalMode ? 'max-w-6xl w-full h-[780px]' : 'h-[780px] max-w-6xl'} overflow-hidden flex flex-col relative`}>
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200 flex-shrink-0">
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
              onClick={handleClose}
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
              <div className="border-b border-gray-200 px-6 pt-6">
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
              <div className="flex-1 overflow-y-auto p-4 min-h-0">
                {activeTab === "info" && (
                  <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 h-full">
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
                              <input
                                type="text"
                                value={editableResponsiblePerson}
                                onChange={(e) => setEditableResponsiblePerson(e.target.value)}
                                className="text-sm border border-gray-300 rounded px-2 py-1 w-32"
                                placeholder="Není přiřazena"
                              />
                            ) : (
                              <span className="text-sm text-gray-900">
                                {order.responsiblePerson || "Není přiřazena"}
                              </span>
                            )}
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
                                className="text-sm border border-gray-300 rounded px-2 py-1 w-24"
                                placeholder="202412"
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
                              <span className="text-sm text-gray-500">Expirace:</span>
                            </div>
                            {canEditFields ? (
                              <input
                                type="date"
                                value={editableExpirationDate}
                                onChange={(e) => setEditableExpirationDate(e.target.value)}
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
                            <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-3">
                              <div className="flex items-start space-x-2">
                                <div className="p-1 bg-yellow-100 rounded-full mt-1">
                                  <StickyNote className="h-3 w-3 text-yellow-600" />
                                </div>
                                <div className="flex-1 min-w-0">
                                  <div className="flex items-center justify-between mb-1">
                                    <span className="text-sm font-medium text-gray-900">
                                      {order.notes[order.notes.length - 1].createdByUser || "Neznámý"}
                                    </span>
                                    <span className="text-xs text-gray-500">
                                      {formatDateTime(order.notes[order.notes.length - 1].createdAt)}
                                    </span>
                                  </div>
                                  <p className="text-sm text-gray-800 whitespace-pre-wrap">
                                    {order.notes[order.notes.length - 1].text}
                                  </p>
                                </div>
                              </div>
                            </div>
                          ) : (
                            <p className="text-gray-500 text-sm italic">
                              Zatím nejsou žádné poznámky
                            </p>
                          )}
                        </div>

                        {/* Add New Note */}
                        <div className="mt-3">
                          <label htmlFor="newNote" className="block text-sm font-medium text-gray-700 mb-2">
                            Přidat poznámku:
                          </label>
                          <textarea
                            id="newNote"
                            value={newNote}
                            onChange={(e) => setNewNote(e.target.value)}
                            placeholder="Napište poznámku..."
                            className="w-full h-16 px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 resize-none text-sm"
                            rows={2}
                          />
                        </div>

                      </div>

                      
                      </div>

                    {/* Right Panel - Semi-product and Products */}
                    <div className="flex flex-col space-y-4">
                      {/* Semi-product Highlighted Row */}
                      <div className="bg-blue-50 rounded-lg p-3">
                        {order.semiProduct ? (
                          <div className="flex items-center justify-between">
                            <div className="flex-1">
                              <div className="text-sm font-medium text-gray-900 mb-1">
                                {order.semiProduct.productName || "Bez názvu"}
                              </div>
                              <div className="text-sm text-gray-600">
                                {order.semiProduct.productCode || "Bez kódu"}
                              </div>
                            </div>
                            <div className="text-right ml-4">
                              {canEditFields ? (
                                <input
                                  type="number"
                                  value={editableSemiProductQuantity}
                                  onChange={(e) => setEditableSemiProductQuantity(e.target.value)}
                                  className="text-lg font-bold text-gray-900 bg-white border border-gray-300 rounded px-2 py-1 w-32 text-center"
                                  min="0"
                                  step="1"
                                />
                              ) : (
                                <div className="text-lg font-bold text-gray-900">
                                  {order.semiProduct.plannedQuantity || "0"}
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
                                        product.plannedQuantity || "-"
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
        <div className="border-t border-gray-200 p-4 flex-shrink-0">
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
            
            {/* Close and Save buttons on the right */}
            <div className="flex items-center space-x-2">
            <button
              onClick={handleClose}
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
      <div className="min-h-screen bg-gray-50">
        <div className="max-w-6xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          {content}
        </div>
      </div>
    );
  }
};

export default ManufactureOrderDetail;