import React, { useEffect, useState } from 'react';
import { X, Package, Calendar, MapPin, User, Clock, Box, Tag, AlertCircle, ArrowLeft, ArrowRight, Loader2, Trash2 } from 'lucide-react';
import { useTransportBoxByIdQuery, useChangeTransportBoxState } from '../../api/hooks/useTransportBoxes';
import { useCatalogAutocomplete } from '../../api/hooks/useCatalogAutocomplete';
import { CatalogItemDto, ProductType, TransportBoxState } from '../../api/generated/api-client';
import AddItemToBoxModal from './AddItemToBoxModal';
import LocationSelectionModal from './LocationSelectionModal';
import { useToast } from '../../contexts/ToastContext';

// Type-safe interface for accessing API client internals
interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

interface TransportBoxDetailProps {
  boxId: number | null;
  isOpen: boolean;
  onClose: () => void;
}

// State labels mapping - using enum keys
const stateLabels: Record<string, string> = {
  'New': 'Nový',
  'Opened': 'Otevřený',
  'InTransit': 'V přepravě',
  'Received': 'Přijatý',
  'Stocked': 'Naskladněný',
  'Reserve': 'V rezervě',
  'Closed': 'Uzavřený',
  'Error': 'Chyba',
};

// State transitions are now handled by backend API

const stateColors: Record<string, string> = {
  'New': 'bg-gray-100 text-gray-800',
  'Opened': 'bg-blue-100 text-blue-800',
  'InTransit': 'bg-yellow-100 text-yellow-800',
  'Received': 'bg-purple-100 text-purple-800',
  'Stocked': 'bg-green-100 text-green-800',
  'Reserve': 'bg-indigo-100 text-indigo-800',
  'Closed': 'bg-gray-100 text-gray-800',
  'Error': 'bg-red-100 text-red-800',
};

const TransportBoxDetail: React.FC<TransportBoxDetailProps> = ({ boxId, isOpen, onClose }) => {
  const { data: boxData, isLoading, error, refetch } = useTransportBoxByIdQuery(boxId || 0, boxId !== null);
  const [activeTab, setActiveTab] = useState<'items' | 'history'>('items');
  const changeStateMutation = useChangeTransportBoxState();
  const { showError, showSuccess } = useToast();

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
    }
  }, [isOpen, boxId, boxData?.transportBox?.description]);


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
      showSuccess('Box otevřen', `Číslo boxu ${trimmedInput} bylo úspěšně přiřazeno a box otevřen.`);
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
          showSuccess('Položka odstraněna', 'Položka byla úspěšně odstraněna z boxu.');
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
        
        showSuccess('Položka přidána', `Položka ${selectedProduct.productName} byla úspěšně přidána do boxu.`);
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
            {/* Box Number Input or Display - Top Right Corner */}
            {boxData?.transportBox && (
              <div className="flex flex-col items-end">
                {boxData.transportBox.state === 'New' ? (
                  // Show input form for New state
                  <form onSubmit={handleBoxNumberSubmit} className="flex flex-col items-end">
                    <div className="flex items-center gap-2">
                      <label htmlFor="boxNumberInput" className="text-sm font-medium text-gray-700">
                        Číslo boxu:
                      </label>
                      <div className="relative">
                        <input
                          id="boxNumberInput"
                          type="text"
                          value={boxNumberInput}
                          onChange={(e) => setBoxNumberInput(e.target.value.toUpperCase())}
                          placeholder="B001"
                          maxLength={4}
                          className={`w-20 px-3 py-2 text-lg font-mono border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent ${
                            boxNumberError ? 'border-red-300' : 'border-gray-300'
                          }`}
                          style={{ fontSize: '16px' }} // Prevent iOS zoom on focus
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
                      {boxData.transportBox.code || '---'}
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
            <div className="bg-gray-50 p-4 rounded-lg">
              <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center gap-2">
                <Box className="h-5 w-5" />
                Základní informace
              </h3>
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700">ID</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.id}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Kód</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.code || '-'}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Stav</label>
                  <span className={`mt-1 inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                    stateColors[boxData.transportBox.state || ''] || 'bg-gray-100 text-gray-800'
                  }`}>
                    {stateLabels[boxData.transportBox.state || ''] || boxData.transportBox.state}
                  </span>
                </div>
                {/* Location - only show in Reserve state */}
                {boxData.transportBox.state === 'Reserve' && (
                  <div>
                    <label className="block text-sm font-medium text-gray-700">Lokace</label>
                    <p className="mt-1 text-sm text-gray-900 flex items-center gap-1">
                      <MapPin className="h-4 w-4 text-gray-400" />
                      {boxData.transportBox.location || '-'}
                    </p>
                  </div>
                )}
                <div>
                  <label className="block text-sm font-medium text-gray-700">Počet položek</label>
                  <p className="mt-1 text-sm text-gray-900">{boxData.transportBox.itemCount}</p>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700">Poslední změna</label>
                  <p className="mt-1 text-sm text-gray-900 flex items-center gap-1">
                    <Calendar className="h-4 w-4 text-gray-400" />
                    {formatDate(boxData.transportBox.lastStateChanged)}
                  </p>
                </div>
              </div>
              {/* Notes/Description Section */}
              <div className="mt-4">
                <label className="block text-sm font-medium text-gray-700 mb-2">Poznámka k boxu</label>
                {isFormEditable('notes') ? (
                  <>
                    <textarea
                      rows={3}
                      value={descriptionInput}
                      onChange={(e) => handleDescriptionChange(e.target.value)}
                      placeholder="Zadejte poznámku k tomuto boxu..."
                      className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    />
                    <p className="mt-1 text-xs text-gray-600">
                      Poznámka se automaticky uloží při změně stavu boxu.
                      {isDescriptionChanged && <span className="text-orange-600 ml-1">(Máte neuložené změny)</span>}
                    </p>
                  </>
                ) : (
                  <p className="text-sm text-gray-900">
                    {boxData.transportBox.description || 
                      <span className="text-gray-400 italic">Žádná poznámka</span>}
                  </p>
                )}
              </div>

            </div>

            {/* Actions for Opened state - Add items */}
            {isFormEditable('items') && (
              <div className="bg-gray-50 p-4 rounded-lg">
                <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center gap-2">
                  <Package className="h-5 w-5" />
                  Přidat položku
                </h3>
                
                {/* Add Item Section */}
                <div>
                  <div className="grid grid-cols-1 md:grid-cols-12 gap-3">
                    <div className="md:col-span-8">
                      <label className="block text-xs font-medium text-gray-700 mb-1">Produkt/Zboží</label>
                      <div className="relative" data-autocomplete-container>
                        <input
                          type="text"
                          value={selectedProduct ? selectedProduct.productName : productInput}
                          onChange={(e) => {
                            setProductInput(e.target.value);
                            if (selectedProduct && e.target.value !== selectedProduct.productName) {
                              setSelectedProduct(null);
                            }
                          }}
                          placeholder="Začněte psát pro vyhledání..."
                          className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent"
                        />
                        
                        {/* Autocomplete dropdown */}
                        {productInput && !selectedProduct && autocompleteData?.items && autocompleteData.items.length > 0 && (
                          <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg max-h-60 overflow-auto">
                            <div className="py-1">
                              {autocompleteData.items.map((item) => (
                                <button
                                  key={item.productCode}
                                  onClick={() => {
                                    setSelectedProduct(item);
                                    setProductInput(item.productName || '');
                                  }}
                                  className="w-full px-3 py-2 text-left hover:bg-gray-50 focus:outline-none focus:bg-gray-50"
                                >
                                  <div className="flex items-center space-x-3">
                                    <Package className="h-4 w-4 text-gray-400 flex-shrink-0" />
                                    <div className="min-w-0 flex-1">
                                      <div className="text-gray-900 font-medium truncate">
                                        {item.productName}
                                      </div>
                                      <div className="text-xs text-gray-500 mt-1">
                                        <span className="font-mono">{item.productCode}</span> • Sklad: {item.stock?.available || 0}
                                      </div>
                                    </div>
                                  </div>
                                </button>
                              ))}
                            </div>
                          </div>
                        )}
                        
                        {/* Loading indicator */}
                        {autocompleteLoading && productInput && (
                          <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg">
                            <div className="px-3 py-2 text-sm text-gray-500">
                              Načítám...
                            </div>
                          </div>
                        )}
                        
                        {/* No results */}
                        {productInput && !autocompleteLoading && autocompleteData?.items && autocompleteData.items.length === 0 && !selectedProduct && (
                          <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg">
                            <div className="px-3 py-2 text-sm text-gray-500">
                              Žádné položky nenalezeny
                            </div>
                          </div>
                        )}
                      </div>
                    </div>
                    <div className="md:col-span-2">
                      <label className="block text-xs font-medium text-gray-700 mb-1">Množství</label>
                      <input
                        type="number"
                        value={quantityInput}
                        onChange={(e) => setQuantityInput(e.target.value)}
                        step="0.01"
                        min="0"
                        placeholder="0"
                        className="w-full px-3 py-2 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-green-500 focus:border-transparent"
                      />
                    </div>
                    <div className="md:col-span-2 flex items-end">
                      <button
                        type="button"
                        onClick={handleAddItem}
                        disabled={!selectedProduct || !quantityInput || parseFloat(quantityInput) <= 0}
                        className="w-full px-3 py-2 text-sm font-medium text-white bg-green-600 rounded-md hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
                      >
                        Přidat
                      </button>
                    </div>
                  </div>
                  <p className="mt-2 text-xs text-gray-600">
                    Začněte psát název nebo kód produktu/zboží pro vyhledání. Po výběru položky zadejte množství pro přidání do boxu.
                    {selectedProduct && (
                      <span className="text-green-600 ml-1">
                        ✓ Vybrán: {selectedProduct.productName} ({selectedProduct.productCode})
                      </span>
                    )}
                  </p>
                </div>
              </div>
            )}

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
                  <div>
                    {boxData.transportBox.items && boxData.transportBox.items.length > 0 ? (
                      <div className="overflow-x-auto">
                        <table className="w-full table-fixed divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="w-32 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Kód produktu
                              </th>
                              <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Název produktu
                              </th>
                              <th className="w-20 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Množství
                              </th>
                              <th className="w-28 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Přidáno
                              </th>
                              <th className="w-20 px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Přidal
                              </th>
                              {isFormEditable('items') && (
                                <th className="w-16 px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                                  Akce
                                </th>
                              )}
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {boxData.transportBox.items.map((item) => (
                              <tr key={item.id} className="hover:bg-gray-50">
                                <td className="px-4 py-4 text-sm font-medium text-gray-900 font-mono">
                                  {item.productCode}
                                </td>
                                <td className="px-4 py-4 text-sm text-gray-900">
                                  <div className="truncate" title={item.productName || '-'}>
                                    {item.productName || '-'}
                                  </div>
                                </td>
                                <td className="px-4 py-4 text-sm text-gray-900 text-right">
                                  {item.amount}
                                </td>
                                <td className="px-4 py-4 text-sm text-gray-900">
                                  <div className="text-xs">
                                    {formatDate(item.dateAdded)}
                                  </div>
                                </td>
                                <td className="px-4 py-4 text-sm text-gray-900">
                                  <div className="truncate text-xs" title={item.userAdded || '-'}>
                                    {item.userAdded || '-'}
                                  </div>
                                </td>
                                {isFormEditable('items') && (
                                  <td className="px-4 py-4 text-right">
                                    <button
                                      onClick={() => item.id && handleRemoveItem(item.id)}
                                      disabled={!item.id}
                                      className="text-red-600 hover:text-red-900 p-1 rounded hover:bg-red-50 disabled:opacity-50 disabled:cursor-not-allowed"
                                      title="Odebrat položku"
                                    >
                                      <Trash2 className="h-4 w-4" />
                                    </button>
                                  </td>
                                )}
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <div className="text-center py-8">
                        <Tag className="mx-auto h-12 w-12 text-gray-400" />
                        <h3 className="mt-2 text-sm font-medium text-gray-900">Žádné položky</h3>
                        <p className="mt-1 text-sm text-gray-500">
                          Tento transportní box neobsahuje žádné položky.
                        </p>
                      </div>
                    )}
                  </div>
                )}
                
                {activeTab === 'history' && (
                  <div>
                    {boxData.transportBox.stateLog && boxData.transportBox.stateLog.length > 0 ? (
                      <div className="overflow-x-auto">
                        <table className="min-w-full divide-y divide-gray-200">
                          <thead className="bg-gray-50">
                            <tr>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Datum
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Stav
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Uživatel
                              </th>
                              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Popis
                              </th>
                            </tr>
                          </thead>
                          <tbody className="bg-white divide-y divide-gray-200">
                            {boxData.transportBox.stateLog.map((log) => (
                              <tr key={log.id} className="hover:bg-gray-50">
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  <div className="flex items-center gap-1">
                                    <Calendar className="h-4 w-4 text-gray-400" />
                                    {formatDate(log.stateDate)}
                                  </div>
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap">
                                  <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                                    stateColors[log.state || ''] || 'bg-gray-100 text-gray-800'
                                  }`}>
                                    {stateLabels[log.state || ''] || log.state}
                                  </span>
                                </td>
                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                                  <div className="flex items-center gap-1">
                                    <User className="h-4 w-4 text-gray-400" />
                                    {log.user || '-'}
                                  </div>
                                </td>
                                <td className="px-6 py-4 text-sm text-gray-900">
                                  {log.description || '-'}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    ) : (
                      <div className="text-center py-8">
                        <Clock className="mx-auto h-12 w-12 text-gray-400" />
                        <h3 className="mt-2 text-sm font-medium text-gray-900">Žádná historie</h3>
                        <p className="mt-1 text-sm text-gray-500">
                          Pro tento transportní box není k dispozici historie stavů.
                        </p>
                      </div>
                    )}
                  </div>
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
        <div className="pt-6 border-t border-gray-200">

          {/* Action Buttons */}
          <div className="flex items-center justify-between">
            {/* Close Button */}
            <button
              onClick={onClose}
              className="px-4 py-2 bg-gray-200 text-gray-800 rounded-md hover:bg-gray-300 transition-colors"
            >
              Zavřít
            </button>
            
            {/* State Transition Flow: Previous → Current → Next */}
            {boxData?.transportBox && (() => {
              const BUTTON_HEIGHT = 80; // Total height in pixels for all transition groups
              
              const previousTransitions = boxData.transportBox.allowedTransitions?.filter(transition => 
                transition.newState && transition.transitionType === 'Previous'
              ) || [];
              
              const nextTransitions = boxData.transportBox.allowedTransitions?.filter(transition => 
                transition.newState && transition.transitionType !== 'Previous'
              ) || [];
              
              // Calculate individual button heights based on count
              const prevButtonHeight = previousTransitions.length > 0 ? 
                (BUTTON_HEIGHT - (previousTransitions.length - 1) * 8) / previousTransitions.length : 0; // 8px gap between buttons
              
              const nextButtonHeight = nextTransitions.length > 0 ? 
                (BUTTON_HEIGHT - (nextTransitions.length - 1) * 8) / nextTransitions.length : 0;
              
              return (
                <div className="flex items-center gap-3">
                  {/* Previous Transitions */}
                  {previousTransitions.length > 0 && (
                    <div className="flex flex-col gap-2" style={{ height: `${BUTTON_HEIGHT}px` }}>
                      {previousTransitions.map((transition, index) => (
                        <button
                          key={`prev-${index}-${transition.newState}`}
                          onClick={() => handleStateChange(transition.newState!)}
                          disabled={changeStateMutation.isPending || transition.systemOnly}
                          className={`flex items-center justify-center px-4 py-1 rounded-md transition-colors min-w-32 ${
                            transition.systemOnly
                              ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                              : 'bg-gray-500 text-white hover:bg-gray-600'
                          } disabled:opacity-50 disabled:cursor-not-allowed`}
                          style={{ height: `${prevButtonHeight}px` }}
                          title={transition.systemOnly ? 'Pouze systémový přechod' : `Změnit na: ${stateLabels[transition.newState!] || transition.newState}`}
                        >
                          <div className="flex items-center">
                            {changeStateMutation.isPending ? (
                              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                            ) : (
                              <ArrowLeft className="h-4 w-4 mr-2" />
                            )}
                            <span className="text-sm">{stateLabels[transition.newState!] || transition.newState}</span>
                          </div>
                        </button>
                      ))}
                    </div>
                  )}
                  
                  {/* Current State (display only) */}
                  <div className={`inline-flex flex-col items-center justify-center px-4 py-2 rounded-md border-2 border-dashed min-w-32 ${
                    stateColors[boxData.transportBox.state || ''] || 'bg-gray-100 text-gray-800 border-gray-300'
                  } border-opacity-50 relative`}
                       style={{ height: `${BUTTON_HEIGHT}px` }}>
                    <div className="text-xs text-gray-500 mb-1">AKTUÁLNÍ</div>
                    <div className="flex items-center">
                      <Box className="h-4 w-4 mr-2" />
                      <span className="text-sm">{stateLabels[boxData.transportBox.state || ''] || boxData.transportBox.state}</span>
                    </div>
                  </div>
                  
                  {/* Next Transitions */}
                  {nextTransitions.length > 0 && (
                    <div className="flex flex-col gap-2" style={{ height: `${BUTTON_HEIGHT}px` }}>
                      {nextTransitions.map((transition, index) => (
                        <button
                          key={`next-${index}-${transition.newState}`}
                          onClick={() => handleStateChange(transition.newState!)}
                          disabled={changeStateMutation.isPending || transition.systemOnly}
                          className={`flex items-center justify-center px-4 py-1 rounded-md transition-colors min-w-32 ${
                            transition.systemOnly
                              ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
                              : transition.newState === 'InTransit' || transition.newState === 'Stocked' || transition.newState === 'Closed'
                              ? 'bg-indigo-600 text-white hover:bg-indigo-700'
                              : transition.newState === 'Opened' || transition.newState === 'New'
                              ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                              : 'bg-blue-600 text-white hover:bg-blue-700'
                          } disabled:opacity-50 disabled:cursor-not-allowed`}
                          style={{ height: `${nextButtonHeight}px` }}
                          title={transition.systemOnly ? 'Pouze systémový přechod' : `Změnit na: ${stateLabels[transition.newState!] || transition.newState}`}
                        >
                          <div className="flex items-center">
                            {changeStateMutation.isPending ? (
                              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                            ) : (
                              <ArrowRight className="h-4 w-4 mr-2" />
                            )}
                            <span className="text-sm">{stateLabels[transition.newState!] || transition.newState}</span>
                          </div>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              );
            })()}
          </div>
        </div>
      </div>

      {/* Modal Components */}
      {boxData?.transportBox && (
        <>
          <AddItemToBoxModal
            isOpen={isAddItemModalOpen}
            onClose={() => setIsAddItemModalOpen(false)}
            boxId={boxData.transportBox.id || null}
            onSuccess={handleAddItemSuccess}
          />
          
          <LocationSelectionModal
            isOpen={isLocationSelectionModalOpen}
            onClose={() => setIsLocationSelectionModalOpen(false)}
            boxId={boxData.transportBox.id || null}
            onSuccess={handleLocationSelectionSuccess}
          />
        </>
      )}
    </div>
  );
};

export default TransportBoxDetail;