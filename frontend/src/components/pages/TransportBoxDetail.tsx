import React, { useEffect, useState } from 'react';
import { X, Package, Tag, Clock, AlertCircle } from 'lucide-react';
import { useTransportBoxByIdQuery, useChangeTransportBoxState } from '../../api/hooks/useTransportBoxes';
import { useCatalogAutocomplete } from '../../api/hooks/useCatalogAutocomplete';
import { CatalogItemDto, ProductType, TransportBoxState } from '../../api/generated/api-client';
import { useToast } from '../../contexts/ToastContext';

// Import new components
import TransportBoxInfo from '../transport/box-detail/TransportBoxInfo';
import TransportBoxItems from '../transport/box-detail/TransportBoxItems';
import TransportBoxHistory from '../transport/box-detail/TransportBoxHistory';
import TransportBoxActions from '../transport/box-detail/TransportBoxActions';
import TransportBoxModals from '../transport/box-detail/TransportBoxModals';
import { TransportBoxDetailProps, ApiClientWithInternals } from '../transport/box-detail/TransportBoxTypes';

const TransportBoxDetail: React.FC<TransportBoxDetailProps> = ({ boxId, isOpen, onClose }) => {
  const { data: boxData, isLoading, error, refetch } = useTransportBoxByIdQuery(boxId || 0, boxId !== null);
  const [activeTab, setActiveTab] = useState<'items' | 'history'>('items');
  const changeStateMutation = useChangeTransportBoxState();
  const { showError } = useToast();

  // Modal states
  const [isAddItemModalOpen, setIsAddItemModalOpen] = useState(false);
  const [isLocationSelectionModalOpen, setIsLocationSelectionModalOpen] = useState(false);
  
  // Box number input for New state
  const [boxNumberInput, setBoxNumberInput] = useState('');
  const [boxNumberError, setBoxNumberError] = useState<string | null>(null);
  
  // Description editing
  const [descriptionInput, setDescriptionInput] = useState('');
  const [isDescriptionChanged, setIsDescriptionChanged] = useState(false);
  
  
  // Add item form
  const [productInput, setProductInput] = useState('');
  const [quantityInput, setQuantityInput] = useState('');
  const [selectedProduct, setSelectedProduct] = useState<CatalogItemDto | null>(null);
  const [debouncedProductSearch, setDebouncedProductSearch] = useState('');

  // Debounce product search
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedProductSearch(productInput);
    }, 300);

    return () => clearTimeout(timer);
  }, [productInput]);

  // Fetch autocomplete data for Products (výrobky) and Goods (zboží) only
  const { data: autocompleteData, isLoading: autocompleteLoading } = useCatalogAutocomplete(
    debouncedProductSearch || undefined,
    50,
    [ProductType.Product, ProductType.Goods] // Only show manufactured products and goods for transport boxes
  );

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      const target = event.target as Element;
      if (target && !target.closest('[data-autocomplete-container]')) {
        // Clear search if no product is selected
        if (!selectedProduct) {
          setProductInput('');
        }
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [selectedProduct]);

  // Handle Escape key to close modal
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.addEventListener('keydown', handleKeyDown);
      return () => {
        document.removeEventListener('keydown', handleKeyDown);
      };
    }
  }, [isOpen, onClose]);

  // Reset tab when modal opens
  useEffect(() => {
    if (isOpen) {
      setActiveTab('items');
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
  const isFormEditable = (fieldType: 'items' | 'notes' | 'boxNumber') => {
    const state = boxData?.transportBox?.state;
    if (state === 'New') {
      return fieldType === 'boxNumber';
    } else if (state === 'Opened') {
      return fieldType === 'items' || fieldType === 'notes';
    }
    return false;
  };
  
  // Initialize form when modal opens or box changes
  useEffect(() => {
    if (isOpen) {
      // Initialize box number input
      setBoxNumberInput('');
      setBoxNumberError(null);
      
      // Initialize description input with current description
      setDescriptionInput(boxData?.transportBox?.description || '');
      setIsDescriptionChanged(false);
      
      
      // Reset add item form
      setProductInput('');
      setQuantityInput('');
      setSelectedProduct(null);
      
      // Focus on box number input if state is New
      if (boxData?.transportBox?.state === 'New') {
        setTimeout(() => {
          const boxNumberInput = document.getElementById('boxNumberInput');
          if (boxNumberInput) {
            (boxNumberInput as HTMLInputElement).focus();
          }
        }, 100);
      }
    }
  }, [isOpen, boxId, boxData?.transportBox?.description, boxData?.transportBox?.state]);


  // Handle description change
  const handleDescriptionChange = (value: string) => {
    setDescriptionInput(value);
    setIsDescriptionChanged(value !== (boxData?.transportBox?.description || ''));
  };

  // Handle location selection success
  const handleLocationSelectionSuccess = async () => {
    await handleModalSuccess();
    setIsLocationSelectionModalOpen(false);
  };

  // Handle box number input for New state
  const handleBoxNumberSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!boxNumberInput.trim() || !boxId) return;
    
    setBoxNumberError(null);
    const trimmedInput = boxNumberInput.trim();
    
    // Validate box number format (B + 3 digits)
    if (!/^B\d{3}$/.test(trimmedInput)) {
      setBoxNumberError('Číslo boxu musí mít formát B + 3 číslice (např. B001, B123)');
      return;
    }

    try {
      // Use change state mutation with boxNumber to assign box number and transition to Opened state
      await changeStateMutation.mutateAsync({
        boxId,
        newState: TransportBoxState.Opened,
        boxNumber: trimmedInput
      });
      
      setBoxNumberInput('');
      await handleModalSuccess(); // Refresh data
      // Success state change - no toast needed
    } catch (err) {
      console.error('Error assigning box number:', err);
      const errorMessage = err instanceof Error ? err.message : 'Neočekávaná chyba';
      setBoxNumberError(errorMessage);
      showError('Chyba při otevírání boxu', errorMessage);
    }
  };

  // Handle remove item
  const handleRemoveItem = async (itemId: number) => {
    if (!boxId) return;
    
    try {
      const { getAuthenticatedApiClient } = await import('../../api/client');
      
      const apiClient = getAuthenticatedApiClient() as unknown as ApiClientWithInternals;
      
      // Use authenticated API client for DELETE request
      const baseUrl = apiClient.baseUrl;
      const fullUrl = `${baseUrl}/api/transport-boxes/${boxId}/items/${itemId}`;
      const response = await apiClient.http.fetch(fullUrl, {
        method: 'DELETE',
        headers: {
          'Content-Type': 'application/json'
        }
      });
      
      if (response.ok) {
        const result = await response.json();
        if (result.success) {
          refetch();
          // Success item removal - no toast needed
        } else {
          const errorMessage = result.errorMessage || 'Neočekávaná chyba';
          showError('Chyba při odstraňování položky', errorMessage);
        }
      } else {
        showError('Chyba při odstraňování položky', response.statusText || 'Neočekávaná chyba');
      }
    } catch (error) {
      console.error('Error removing item:', error);
      const errorMessage = error instanceof Error ? error.message : 'Neočekávaná chyba';
      showError('Chyba při odstraňování položky', errorMessage);
    }
  };

  // Handle add item
  const handleAddItem = async () => {
    if (!selectedProduct || !quantityInput || !boxId) return;
    
    const quantity = parseFloat(quantityInput);
    if (quantity <= 0) {
      console.error('Quantity must be positive');
      return;
    }
    
    try {
      const { getAuthenticatedApiClient } = await import('../../api/client');
      const { AddItemToBoxRequest } = await import('../../api/generated/api-client');
      
      const apiClient = await getAuthenticatedApiClient();
      const request = new AddItemToBoxRequest({
        boxId: boxId,
        productCode: selectedProduct.productCode || '',
        productName: selectedProduct.productName || '',
        amount: quantity
      });
      
      const response = await apiClient.transportBox_AddItemToBox(boxId, request);
      
      if (response.success) {
        // Clear form and refresh data
        setProductInput('');
        setQuantityInput('');
        setSelectedProduct(null);
        refetch();
        
        // Success item addition - no toast needed
      } else {
        const errorMessage = response.errorMessage || 'Neočekávaná chyba při přidávání položky';
        showError('Chyba při přidávání položky', errorMessage);
      }
    } catch (error) {
      console.error('Error adding item:', error);
      const errorMessage = error instanceof Error ? error.message : 'Neočekávaná chyba při přidávání položky';
      showError('Chyba při přidávání položky', errorMessage);
    }
  };


  // Handle state change - convert string state to enum
  const handleStateChange = async (newStateString: string) => {
    if (!boxId) return;
    
    // Handle special cases for state changes that require user input
    if (newStateString === 'Reserve') {
      // For Reserve transition, open the location selection modal
      setIsLocationSelectionModalOpen(true);
      return;
    }
    
    // Convert string to enum value
    const newState = TransportBoxState[newStateString as keyof typeof TransportBoxState];
    
    try {
      // Include description if it was changed
      const request: any = {
        boxId,
        newState,
        description: isDescriptionChanged ? descriptionInput : undefined
      };
      
      await changeStateMutation.mutateAsync(request);
      
      // Clear changed flags - cache invalidation is handled by mutation hook
      if (isDescriptionChanged) {
        setIsDescriptionChanged(false);
      }
      
      // State changed successfully - no toast needed for routine state changes
      
    } catch (error) {
      console.error('Failed to change state:', error);
      const errorMessage = error instanceof Error ? error.message : 'Neočekávaná chyba při změně stavu';
      showError('Chyba při změně stavu', errorMessage);
    }
  };



  const formatDate = (dateString: string | Date | undefined) => {
    if (!dateString) return '-';
    const date = typeof dateString === 'string' ? new Date(dateString) : dateString;
    return date.toLocaleDateString('cs-CZ', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-gray-600 bg-opacity-50 overflow-y-auto h-full w-full z-50">
      <div className="relative top-4 mx-auto p-5 border w-11/12 max-w-6xl shadow-lg rounded-md bg-white mb-8">
        {/* Header */}
        <div className="flex items-start justify-between mb-6">
          <div className="flex items-center gap-3">
            <Package className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                Detail transportního boxu
              </h2>
            </div>
          </div>
          
          <div className="flex items-start gap-4">
            {/* Box Number Display - Top Right Corner */}
            {boxData?.transportBox && boxData.transportBox.state !== 'New' && (
              <div className="flex flex-col items-end">
                <div className="text-sm font-medium text-gray-700 mb-1">
                  Číslo boxu:
                </div>
                <div className="px-4 py-2 text-xl font-mono font-bold text-indigo-600 bg-indigo-50 border-2 border-indigo-200 rounded-md">
                  {boxData.transportBox.code || '---'}
                </div>
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

        {/* Content */}
        {isLoading ? (
          <div className="flex items-center justify-center py-8">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600"></div>
            <span className="ml-2 text-gray-600">Načítám detail boxu...</span>
          </div>
        ) : error ? (
          <div className="flex items-center gap-2 text-red-600 py-8">
            <AlertCircle className="h-5 w-5" />
            <span>Chyba při načítání detailu boxu</span>
          </div>
        ) : boxData?.transportBox ? (
          <div className="space-y-6">
            {/* Basic Information */}
            <TransportBoxInfo
              transportBox={boxData.transportBox}
              boxNumberInput={boxNumberInput}
              setBoxNumberInput={setBoxNumberInput}
              boxNumberError={boxNumberError}
              descriptionInput={descriptionInput}
              handleDescriptionChange={handleDescriptionChange}
              isDescriptionChanged={isDescriptionChanged}
              isFormEditable={isFormEditable}
              handleBoxNumberSubmit={handleBoxNumberSubmit}
              formatDate={formatDate}
            />

            {/* Tab Navigation */}
            <div className="bg-white border border-gray-200 rounded-lg">
              <div className="border-b border-gray-200">
                <nav className="-mb-px flex space-x-8 px-4" aria-label="Tabs">
                  <button
                    onClick={() => setActiveTab('items')}
                    className={`whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm flex items-center gap-2 ${
                      activeTab === 'items'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <Tag className="h-4 w-4" />
                    Položky ({boxData.transportBox.items?.length || 0})
                  </button>
                  <button
                    onClick={() => setActiveTab('history')}
                    className={`whitespace-nowrap py-4 px-1 border-b-2 font-medium text-sm flex items-center gap-2 ${
                      activeTab === 'history'
                        ? 'border-indigo-500 text-indigo-600'
                        : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                    }`}
                  >
                    <Clock className="h-4 w-4" />
                    Historie ({boxData.transportBox.stateLog?.length || 0})
                  </button>
                </nav>
              </div>
              
              <div className="p-0">
                {activeTab === 'items' && (
                  <TransportBoxItems
                    transportBox={boxData.transportBox}
                    isFormEditable={isFormEditable}
                    formatDate={formatDate}
                    handleRemoveItem={handleRemoveItem}
                    productInput={productInput}
                    setProductInput={setProductInput}
                    quantityInput={quantityInput}
                    setQuantityInput={setQuantityInput}
                    selectedProduct={selectedProduct}
                    setSelectedProduct={setSelectedProduct}
                    handleAddItem={handleAddItem}
                    autocompleteData={autocompleteData}
                    autocompleteLoading={autocompleteLoading}
                  />
                )}
                
                {activeTab === 'history' && (
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
            <h3 className="mt-2 text-sm font-medium text-gray-900">Box nenalezen</h3>
            <p className="mt-1 text-sm text-gray-500">
              Transportní box s ID {boxId} nebyl nalezen.
            </p>
          </div>
        )}

        {/* Footer with Action Buttons */}
        {boxData?.transportBox && (
          <TransportBoxActions
            transportBox={boxData.transportBox}
            changeStateMutation={changeStateMutation}
            handleStateChange={handleStateChange}
            onClose={onClose}
          />
        )}
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
    </div>
  );
};

export default TransportBoxDetail;