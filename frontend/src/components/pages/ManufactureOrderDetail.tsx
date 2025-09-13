import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { useParams, useNavigate, Navigate } from "react-router-dom";
import {
  X,
  Calendar,
  User,
  Clock,
  Loader2,
  AlertCircle,
  Edit,
  Info,
  ScrollText,
  FileText,
  StickyNote,
  Factory,
  ShoppingCart,
  Settings,
  ArrowLeft,
} from "lucide-react";
import {
  useManufactureOrderDetailQuery,
  ManufactureOrderState,
} from "../../api/hooks/useManufactureOrders";

interface ManufactureOrderDetailProps {
  orderId?: number;
  isOpen?: boolean;
  onClose?: () => void;
  onEdit?: (orderId: number) => void;
}


const stateColors: Record<ManufactureOrderState, string> = {
  [ManufactureOrderState.Draft]: "bg-gray-100 text-gray-800",
  [ManufactureOrderState.SemiProductPlanned]: "bg-blue-100 text-blue-800",
  [ManufactureOrderState.SemiProductManufacture]: "bg-yellow-100 text-yellow-800",
  [ManufactureOrderState.ProductsPlanned]: "bg-indigo-100 text-indigo-800",
  [ManufactureOrderState.ProductsManufacture]: "bg-orange-100 text-orange-800",
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
  
  // Determine if this is modal mode (has propOrderId) or page mode (uses URL param)
  const isModalMode = propOrderId !== undefined;
  const urlOrderId = id ? parseInt(id, 10) : 0;
  const orderId = isModalMode ? propOrderId : urlOrderId;
  
  // For page mode, create a close handler that navigates back
  const handleClose = useCallback(() => {
    if (onClose) {
      onClose();
    } else {
      navigate("/manufacturing/orders");
    }
  }, [onClose, navigate]);
  
  // Helper function to get translated state label
  const getStateLabel = (state: ManufactureOrderState): string => {
    return t(`manufacture.states.${ManufactureOrderState[state]}`);
  };
  
  // Helper function to get translated audit action label
  const getAuditActionLabel = (action: any): string => {
    const actionName = typeof action === 'string' ? action : action?.toString();
    return t(`manufacture.auditActions.${actionName}`) || actionName || '-';
  };
  
  // Tab state
  const [activeTab, setActiveTab] = useState<"info" | "semiproducts" | "products" | "notes" | "log">("info");

  // Fetch order details
  const {
    data: orderData,
    isLoading: orderLoading,
    error: orderError,
  } = useManufactureOrderDetailQuery(orderId || 0);

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

  const order = orderData?.order;

  const content = (
    <div className={`bg-white ${isModalMode ? 'rounded-lg shadow-xl' : ''} max-w-7xl w-full ${isModalMode ? 'max-h-[90vh]' : 'min-h-screen'} overflow-hidden flex flex-col`}>
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
                    onClick={() => setActiveTab("semiproducts")}
                    className={`${
                      activeTab === "semiproducts"
                        ? "border-indigo-500 text-indigo-600"
                        : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <Settings className="h-4 w-4 mr-2" />
                    Polotovary ({order.semiProducts?.length || 0})
                  </button>
                  <button
                    onClick={() => setActiveTab("products")}
                    className={`${
                      activeTab === "products"
                        ? "border-indigo-500 text-indigo-600"
                        : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                    } whitespace-nowrap py-2 px-1 border-b-2 font-medium text-sm flex items-center`}
                  >
                    <ShoppingCart className="h-4 w-4 mr-2" />
                    Produkty ({order.products?.length || 0})
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
              <div className="flex-1 overflow-y-auto p-6 min-h-0">
                {activeTab === "info" && (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                    {/* Basic Information */}
                    <div className="bg-gray-50 rounded-lg p-6">
                      <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                        <Info className="h-5 w-5 mr-2 text-indigo-600" />
                        Základní informace
                      </h3>
                      <div className="space-y-3">
                        <div className="flex items-center">
                          <FileText className="h-4 w-4 text-gray-400 mr-3" />
                          <div className="flex-1">
                            <span className="text-sm font-medium text-gray-500">Číslo zakázky:</span>
                            <span className="ml-2 text-sm text-gray-900 font-medium">
                              {order.orderNumber}
                            </span>
                          </div>
                        </div>
                        <div className="flex items-center">
                          <Factory className="h-4 w-4 text-gray-400 mr-3" />
                          <div className="flex-1">
                            <span className="text-sm font-medium text-gray-500">Stav:</span>
                            <span className="ml-2">
                              {order.state !== undefined && (
                                <span
                                  className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${stateColors[order.state]}`}
                                >
                                  {getStateLabel(order.state)}
                                </span>
                              )}
                            </span>
                          </div>
                        </div>
                        <div className="flex items-center">
                          <User className="h-4 w-4 text-gray-400 mr-3" />
                          <div className="flex-1">
                            <span className="text-sm font-medium text-gray-500">Odpovědná osoba:</span>
                            <span className="ml-2 text-sm text-gray-900">
                              {order.responsiblePerson || "-"}
                            </span>
                          </div>
                        </div>
                      </div>
                    </div>

                    {/* Timestamps */}
                    <div className="bg-gray-50 rounded-lg p-6">
                      <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                        <Clock className="h-5 w-5 mr-2 text-indigo-600" />
                        Časové údaje
                      </h3>
                      <div className="space-y-3">
                        <div className="flex items-center">
                          <Calendar className="h-4 w-4 text-gray-400 mr-3" />
                          <div className="flex-1">
                            <span className="text-sm font-medium text-gray-500">Vytvořeno:</span>
                            <span className="ml-2 text-sm text-gray-900">
                              {formatDateTime(order.createdDate)}
                            </span>
                          </div>
                        </div>
                        <div className="flex items-center">
                          <Calendar className="h-4 w-4 text-gray-400 mr-3" />
                          <div className="flex-1">
                            <span className="text-sm font-medium text-gray-500">Změna stavu:</span>
                            <span className="ml-2 text-sm text-gray-900">
                              {formatDateTime(order.stateChangedAt)}
                            </span>
                          </div>
                        </div>
                        {order.semiProductPlannedDate && (
                          <div className="flex items-center">
                            <Calendar className="h-4 w-4 text-gray-400 mr-3" />
                            <div className="flex-1">
                              <span className="text-sm font-medium text-gray-500">Polotovary plánovány:</span>
                              <span className="ml-2 text-sm text-gray-900">
                                {formatDate(order.semiProductPlannedDate)}
                              </span>
                            </div>
                          </div>
                        )}
                        {order.productPlannedDate && (
                          <div className="flex items-center">
                            <Calendar className="h-4 w-4 text-gray-400 mr-3" />
                            <div className="flex-1">
                              <span className="text-sm font-medium text-gray-500">Produkty plánovány:</span>
                              <span className="ml-2 text-sm text-gray-900">
                                {formatDate(order.productPlannedDate)}
                              </span>
                            </div>
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Additional info could be added here if needed */}
                  </div>
                )}

                {activeTab === "semiproducts" && (
                  <div>
                    <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                      <Settings className="h-5 w-5 mr-2 text-indigo-600" />
                      Polotovary
                    </h3>
                    {order.semiProducts && order.semiProducts.length > 0 ? (
                      <div className="bg-white shadow rounded-lg overflow-hidden">
                        <table className="min-w-full divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Kód produktu
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Název produktu
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Plánované množství
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Skutečné množství
                              </th>
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {order.semiProducts.map((semiProduct, index) => (
                              <tr key={index}>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  {semiProduct.productCode || "-"}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {semiProduct.productName || "-"}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {semiProduct.plannedQuantity || "-"}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {semiProduct.actualQuantity || "-"}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <p className="text-gray-500 text-center py-8">Žádné polotovary nebyly definovány.</p>
                    )}
                  </div>
                )}

                {activeTab === "products" && (
                  <div>
                    <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
                      <ShoppingCart className="h-5 w-5 mr-2 text-indigo-600" />
                      Produkty
                    </h3>
                    {order.products && order.products.length > 0 ? (
                      <div className="bg-white shadow rounded-lg overflow-hidden">
                        <table className="min-w-full divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Kód produktu
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Název produktu
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Plánované množství
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Skutečné množství
                              </th>
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {order.products.map((product, index) => (
                              <tr key={index}>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  {product.productCode || "-"}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {product.productName || "-"}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {product.plannedQuantity || "-"}
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                  {product.actualQuantity || "-"}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <p className="text-gray-500 text-center py-8">Žádné produkty nebyly definovány.</p>
                    )}
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
        <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          {content}
        </div>
      </div>
    );
  }
};

export default ManufactureOrderDetail;