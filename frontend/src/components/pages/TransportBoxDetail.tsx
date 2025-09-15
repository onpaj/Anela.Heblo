import React, { useEffect, useState } from "react";
import { X, Package, Clock, AlertCircle } from "lucide-react";
import {
  useTransportBoxByIdQuery,
  useChangeTransportBoxState,
} from "../../api/hooks/useTransportBoxes";
import { useLastAddedItem } from "../../api/hooks/useLastAddedItem";
import {
  CatalogItemDto,
  TransportBoxState,
} from "../../api/generated/api-client";
import { useToast } from "../../contexts/ToastContext";

// Import new components
import TransportBoxInfo from "../transport/box-detail/TransportBoxInfo";
import TransportBoxItems from "../transport/box-detail/TransportBoxItems";
import TransportBoxHistory from "../transport/box-detail/TransportBoxHistory";
import TransportBoxActions from "../transport/box-detail/TransportBoxActions";
import TransportBoxModals from "../transport/box-detail/TransportBoxModals";
import QuickAddLastItemModal from "../transport/QuickAddLastItemModal";
import {
  TransportBoxDetailProps,
  ApiClientWithInternals,
} from "../transport/box-detail/TransportBoxTypes";

const TransportBoxDetail: React.FC<TransportBoxDetailProps> = ({
  boxId,
  isOpen,
  onClose,
}) => {
  const {
    data: boxData,
    isLoading,
    error,
    refetch,
  } = useTransportBoxByIdQuery(boxId || 0, boxId !== null);
  const [activeTab, setActiveTab] = useState<"info" | "history">("info");
  const changeStateMutation = useChangeTransportBoxState();
  const { showError } = useToast();

  // Last added item tracking
  const { lastAddedItem, saveLastAddedItem } = useLastAddedItem();

  // Modal states
  const [isAddItemModalOpen, setIsAddItemModalOpen] = useState(false);
  const [isQuickAddModalOpen, setIsQuickAddModalOpen] = useState(false);
  const [isLocationSelectionModalOpen, setIsLocationSelectionModalOpen] =
    useState(false);

  // Box number input for New state
  const [boxNumberInput, setBoxNumberInput] = useState("");
  const [boxNumberError, setBoxNumberError] = useState<string | null>(null);

  // Description editing
  const [descriptionInput, setDescriptionInput] = useState("");
  const [isDescriptionChanged, setIsDescriptionChanged] = useState(false);

  // Add item form
  const [quantityInput, setQuantityInput] = useState("");
  const [selectedProduct, setSelectedProduct] = useState<CatalogItemDto | null>(
    null,
  );

  // Handle Escape key to close modal
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
      return () => {
        document.removeEventListener("keydown", handleKeyDown);
      };
    }
  }, [isOpen, onClose]);

  // Reset tab when modal opens
  useEffect(() => {
    if (isOpen) {
      setActiveTab("info");
    }
  }, [isOpen]);

  // Handle modal success - refresh data and close modal
  const handleModalSuccess = async () => {
    await refetch();
  };

  const handleAddItemSuccess = async () => {
    await handleModalSuccess();
    setIsAddItemModalOpen(false);
  };

  // Check if form fields should be editable
  const isFormEditable = (fieldType: "items" | "notes" | "boxNumber") => {
    const state = boxData?.transportBox?.state;
    
    // Notes can always be edited regardless of box state
    if (fieldType === "notes") {
      return true;
    }
    
    if (state === "New") {
      return fieldType === "boxNumber";
    } else if (state === "Opened") {
      return fieldType === "items";
    }
    return false;
  };

  // Initialize form when modal opens or box changes
  useEffect(() => {
    if (isOpen) {
      // Initialize box number input
      setBoxNumberInput("");
      setBoxNumberError(null);

      // Initialize description input with current description
      setDescriptionInput(boxData?.transportBox?.description || "");
      setIsDescriptionChanged(false);

      // Reset add item form
      setQuantityInput("");
      setSelectedProduct(null);

      // Focus on box number input if state is New
      if (boxData?.transportBox?.state === "New") {
        setTimeout(() => {
          const boxNumberInput = document.getElementById("boxNumberInput");
          if (boxNumberInput) {
            (boxNumberInput as HTMLInputElement).focus();
          }
        }, 100);
      }
    }
  }, [
    isOpen,
    boxId,
    boxData?.transportBox?.description,
    boxData?.transportBox?.state,
  ]);

  // Handle description change
  const handleDescriptionChange = (value: string) => {
    setDescriptionInput(value);
    setIsDescriptionChanged(
      value !== (boxData?.transportBox?.description || ""),
    );
  };

  // Handle location selection success
  const handleLocationSelectionSuccess = async () => {
    await handleModalSuccess();
    setIsLocationSelectionModalOpen(false);
    // Auto-close main modal after successful reserve operation
    onClose();
  };

  // Handle box number input for New state
  const handleBoxNumberSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxNumberInput.trim() || !boxId) return;

    setBoxNumberError(null);
    const trimmedInput = boxNumberInput.trim();

    // Validate box number format (B + 3 digits)
    if (!/^B\d{3}$/.test(trimmedInput)) {
      setBoxNumberError(
        "Číslo boxu musí mít formát B + 3 číslice (např. B001, B123)",
      );
      return;
    }

    try {
      // Use change state mutation with boxNumber to assign box number and transition to Opened state
      await changeStateMutation.mutateAsync({
        boxId,
        newState: TransportBoxState.Opened,
        boxNumber: trimmedInput,
      });

      setBoxNumberInput("");
      await handleModalSuccess(); // Refresh data
      // Success state change - no toast needed
    } catch (err) {
      console.error("Error assigning box number:", err);
      const errorMessage =
        err instanceof Error ? err.message : "Neočekávaná chyba";
      setBoxNumberError(errorMessage);
      // Toast is already shown by global error handler - no need to show another one
    }
  };

  // Handle remove item
  const handleRemoveItem = async (itemId: number) => {
    if (!boxId) return;

    try {
      const { getAuthenticatedApiClient } = await import("../../api/client");

      const apiClient =
        getAuthenticatedApiClient() as unknown as ApiClientWithInternals;

      // Use authenticated API client for DELETE request
      const baseUrl = apiClient.baseUrl;
      const fullUrl = `${baseUrl}/api/transport-boxes/${boxId}/items/${itemId}`;
      const response = await apiClient.http.fetch(fullUrl, {
        method: "DELETE",
        headers: {
          "Content-Type": "application/json",
        },
      });

      if (response.ok) {
        const result = await response.json();
        if (result.success) {
          refetch();
          // Success item removal - no toast needed
        } else {
          const errorMessage = result.errorMessage || "Neočekávaná chyba";
          showError("Chyba při odstraňování položky", errorMessage);
        }
      } else {
        showError(
          "Chyba při odstraňování položky",
          response.statusText || "Neočekávaná chyba",
        );
      }
    } catch (error) {
      console.error("Error removing item:", error);
      const errorMessage =
        error instanceof Error ? error.message : "Neočekávaná chyba";
      showError("Chyba při odstraňování položky", errorMessage);
    }
  };

  // Handle add item
  const handleAddItem = async () => {
    if (!selectedProduct || !quantityInput || !boxId) return;

    const quantity = parseFloat(quantityInput);
    if (quantity <= 0) {
      console.error("Quantity must be positive");
      return;
    }

    try {
      const { getAuthenticatedApiClient } = await import("../../api/client");
      const { AddItemToBoxRequest } = await import(
        "../../api/generated/api-client"
      );

      const apiClient = await getAuthenticatedApiClient();
      const request = new AddItemToBoxRequest({
        boxId: boxId,
        productCode: selectedProduct.productCode || "",
        productName: selectedProduct.productName || "",
        amount: quantity,
      });

      const response = await apiClient.transportBox_AddItemToBox(
        boxId,
        request,
      );

      if (response.success) {
        // Save last added item for quick repeat
        saveLastAddedItem({
          productCode: selectedProduct.productCode || "",
          productName: selectedProduct.productName || "",
          amount: quantity,
        });

        // Clear form and refresh data
        setQuantityInput("");
        setSelectedProduct(null);
        refetch();

        // Success item addition - no toast needed
      }
      // If response.success is false, the global error handler will show a toast
    } catch (error) {
      console.error("Error adding item:", error);
      const errorMessage =
        error instanceof Error
          ? error.message
          : "Neočekávaná chyba při přidávání položky";
      showError("Chyba při přidávání položky", errorMessage);
    }
  };

  // Handle quick add last item
  const handleQuickAddSuccess = async () => {
    await refetch();
    setIsQuickAddModalOpen(false);
  };

  // Handle save note/description independently
  const handleSaveNote = async () => {
    if (!boxId || !isDescriptionChanged) return;

    try {
      const { getAuthenticatedApiClient } = await import("../../api/client");
      const { UpdateTransportBoxDescriptionRequest } = await import(
        "../../api/generated/api-client"
      );

      const apiClient = await getAuthenticatedApiClient();
      const request = new UpdateTransportBoxDescriptionRequest({
        description: descriptionInput,
      });

      const response = await apiClient.transportBox_UpdateTransportBoxDescription(
        boxId,
        request
      );

      if (response.success) {
        // Clear changed flags and refresh data
        setIsDescriptionChanged(false);
        await refetch();
        // Success - no toast needed as per existing pattern
      }
      // If response.success is false, the global error handler will show a toast
    } catch (error) {
      console.error("Error saving description:", error);
      const errorMessage =
        error instanceof Error
          ? error.message
          : "Neočekávaná chyba při ukládání poznámky";
      showError("Chyba při ukládání poznámky", errorMessage);
    }
  };

  // Handle state change - convert string state to enum
  const handleStateChange = async (newStateString: string) => {
    if (!boxId) return;

    // Handle special cases for state changes that require user input
    if (newStateString === "Reserve") {
      // For Reserve transition, open the location selection modal
      setIsLocationSelectionModalOpen(true);
      return;
    }

    // Convert string to enum value
    const newState =
      TransportBoxState[newStateString as keyof typeof TransportBoxState];

    try {
      // Include description if it was changed
      const request: any = {
        boxId,
        newState,
        description: isDescriptionChanged ? descriptionInput : undefined,
      };

      await changeStateMutation.mutateAsync(request);

      // Clear changed flags - cache invalidation is handled by mutation hook
      if (isDescriptionChanged) {
        setIsDescriptionChanged(false);
      }

      // Auto-close modal for final states
      if (newStateString === "InTransit" || newStateString === "Reserved") {
        onClose();
        return;
      }

      // State changed successfully - no toast needed for routine state changes
    } catch (error) {
      console.error("Failed to change state:", error);
      const errorMessage =
        error instanceof Error
          ? error.message
          : "Neočekávaná chyba při změně stavu";
      showError("Chyba při změně stavu", errorMessage);
    }
  };

  const formatDate = (dateString: string | Date | undefined) => {
    if (!dateString) return "-";
    const date =
      typeof dateString === "string" ? new Date(dateString) : dateString;
    return date.toLocaleDateString("cs-CZ", {
      day: "2-digit",
      month: "2-digit",
      year: "numeric",
      hour: "2-digit",
      minute: "2-digit",
    });
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
      <div
        className="relative top-2 mx-auto p-3 border w-11/12 max-w-7xl shadow-lg rounded-md bg-white mb-4 flex flex-col"
        style={{ maxHeight: "calc(100vh - 2rem)", minHeight: "900px" }}
      >
        {/* Header - Fixed */}
        <div className="flex items-start justify-between mb-4 flex-shrink-0">
          <div className="flex items-center gap-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                Detail transportního boxu
              </h2>
            </div>
          </div>

          <div className="flex items-start gap-4">
            {/* Box Number Input or Display - Top Right Corner */}
            {boxData?.transportBox && (
              <div className="flex flex-col items-end">
                {boxData.transportBox.state === "New" ? (
                  // Show input form for New state
                  <form
                    onSubmit={handleBoxNumberSubmit}
                    className="flex flex-col items-end"
                  >
                    <div className="flex items-center gap-2">
                      <label
                        htmlFor="boxNumberInput"
                        className="text-sm font-medium text-gray-700"
                      >
                        Číslo boxu:
                      </label>
                      <div className="relative">
                        <input
                          id="boxNumberInput"
                          type="text"
                          value={boxNumberInput}
                          onChange={(e) =>
                            setBoxNumberInput(e.target.value.toUpperCase())
                          }
                          placeholder="B001"
                          maxLength={4}
                          className={`w-20 px-3 py-2 text-lg font-mono border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${
                            boxNumberError
                              ? "border-red-300"
                              : "border-gray-300"
                          }`}
                          style={{ fontSize: "16px" }} // Prevent iOS zoom on focus
                        />
                      </div>
                      <button
                        type="submit"
                        disabled={!boxNumberInput.trim()}
                        className="px-3 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        Přiřadit
                      </button>
                    </div>
                    {boxNumberError && (
                      <div className="mt-1 text-xs text-red-600 max-w-xs text-right">
                        {boxNumberError}
                      </div>
                    )}
                    <div className="mt-1 text-xs text-gray-500 text-right">
                      Zadání čísla otevře box (B + 3 číslice)
                    </div>
                  </form>
                ) : (
                  // Show prominent box number display for all other states
                  <div className="flex flex-col items-end">
                    <div className="text-sm font-medium text-gray-700 mb-1">
                      Číslo boxu:
                    </div>
                    <div className="px-4 py-2 text-xl font-mono font-bold text-indigo-600 bg-indigo-50 border-2 border-indigo-200 rounded-md">
                      {boxData.transportBox.code || "---"}
                    </div>
                  </div>
                )}
              </div>
            )}

            <button
              onClick={onClose}
              className="text-gray-400 hover:text-gray-600 transition-colors"
            >
              <X className="h-6 w-6" />
            </button>
          </div>
        </div>

        {/* Main Layout with fixed right panel and flexible main content */}
        <div className="flex flex-col lg:flex-row gap-6 flex-1 min-h-0">
          {/* Main Content - Flexible Area */}
          <div className="flex-1 min-h-0">
            {isLoading ? (
              <div className="flex items-center justify-center py-8">
                <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
                <span className="ml-2 text-gray-600">
                  Načítám detail boxu...
                </span>
              </div>
            ) : error ? (
              <div className="flex items-center gap-2 text-red-600 py-8">
                <AlertCircle className="h-5 w-5" />
                <span>Chyba při načítání detailu boxu</span>
              </div>
            ) : boxData?.transportBox ? (
              <div className="h-full flex flex-col">
                {/* Tab Navigation */}
                <div className="bg-white border border-gray-200 rounded-lg flex-shrink-0">
                  <div className="border-b border-gray-200">
                    <nav
                      className="-mb-px flex space-x-8 px-4"
                      aria-label="Tabs"
                    >
                      <button
                        onClick={() => setActiveTab("info")}
                        className={`whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm flex items-center gap-2 ${
                          activeTab === "info"
                            ? "border-indigo-500 text-indigo-600"
                            : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                        }`}
                      >
                        <Package className="h-4 w-4" />
                        Základní informace
                      </button>
                      <button
                        onClick={() => setActiveTab("history")}
                        className={`whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm flex items-center gap-2 ${
                          activeTab === "history"
                            ? "border-indigo-500 text-indigo-600"
                            : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
                        }`}
                      >
                        <Clock className="h-4 w-4" />
                        Historie ({boxData.transportBox.stateLog?.length || 0})
                      </button>
                    </nav>
                  </div>

                  {/* Flexible Content Area */}
                  <div className="flex-1 overflow-auto">
                    {activeTab === "info" && (
                      <div className="space-y-4 p-4">
                        {/* Basic Information */}
                        <TransportBoxInfo
                          transportBox={boxData.transportBox}
                          descriptionInput={descriptionInput}
                          handleDescriptionChange={handleDescriptionChange}
                          isDescriptionChanged={isDescriptionChanged}
                          isFormEditable={isFormEditable}
                          formatDate={formatDate}
                          handleSaveNote={handleSaveNote}
                        />

                        {/* Items directly below - no extra container */}
                        <TransportBoxItems
                          transportBox={boxData.transportBox}
                          isFormEditable={isFormEditable}
                          formatDate={formatDate}
                          handleRemoveItem={handleRemoveItem}
                          quantityInput={quantityInput}
                          setQuantityInput={setQuantityInput}
                          selectedProduct={selectedProduct}
                          setSelectedProduct={setSelectedProduct}
                          handleAddItem={handleAddItem}
                          lastAddedItem={lastAddedItem}
                          handleQuickAdd={() => setIsQuickAddModalOpen(true)}
                        />
                      </div>
                    )}

                    {activeTab === "history" && (
                      <TransportBoxHistory
                        transportBox={boxData.transportBox}
                        formatDate={formatDate}
                      />
                    )}
                  </div>
                </div>
              </div>
            ) : (
              <div className="text-center py-8">
                <Package className="mx-auto h-12 w-12 text-gray-400" />
                <h3 className="mt-2 text-sm font-medium text-gray-900">
                  Box nenalezen
                </h3>
                <p className="mt-1 text-sm text-gray-500">
                  Transportní box s ID {boxId} nebyl nalezen.
                </p>
              </div>
            )}
          </div>

          {/* Right Panel - Fixed Navigation and Close Button */}
          <div className="w-full lg:w-80 flex-shrink-0 flex flex-col">
            {/* State Navigation - Takes available space */}
            <div className="flex-1 mb-6">
              {boxData?.transportBox && (
                <TransportBoxActions
                  transportBox={boxData.transportBox}
                  changeStateMutation={changeStateMutation}
                  handleStateChange={handleStateChange}
                />
              )}
            </div>

            {/* Close Button - Fixed at bottom */}
            <div className="pt-4 border-t border-gray-200 flex justify-end">
              <button
                onClick={onClose}
                className="px-6 py-3 text-base font-medium text-gray-700 bg-white border border-gray-300 rounded-lg hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 min-h-[48px] min-w-[120px]"
              >
                Zavřít
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Modal Components */}
      {boxData?.transportBox && (
        <TransportBoxModals
          transportBox={boxData.transportBox}
          isAddItemModalOpen={isAddItemModalOpen}
          setIsAddItemModalOpen={setIsAddItemModalOpen}
          isLocationSelectionModalOpen={isLocationSelectionModalOpen}
          setIsLocationSelectionModalOpen={setIsLocationSelectionModalOpen}
          handleAddItemSuccess={handleAddItemSuccess}
          handleLocationSelectionSuccess={handleLocationSelectionSuccess}
        />
      )}

      {/* Quick Add Last Item Modal */}
      <QuickAddLastItemModal
        isOpen={isQuickAddModalOpen}
        onClose={() => setIsQuickAddModalOpen(false)}
        boxId={boxId}
        lastAddedItem={lastAddedItem}
        onSuccess={handleQuickAddSuccess}
        onItemAdded={saveLastAddedItem}
      />
    </div>
  );
};

export default TransportBoxDetail;
