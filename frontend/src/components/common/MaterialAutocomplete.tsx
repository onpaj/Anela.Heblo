import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { ChevronDown, Check, Package, AlertCircle } from 'lucide-react';
import { useCatalogAutocomplete } from '../../api/hooks/useCatalogAutocomplete';
import { CatalogItemDto, ProductType } from '../../api/generated/api-client';
import { MaterialForPurchaseDto } from '../../api/hooks/useMaterials';

// Adapter function to convert CatalogItemDto to MaterialForPurchaseDto
const catalogItemToMaterial = (item: CatalogItemDto): MaterialForPurchaseDto => ({
  productCode: item.productCode,
  productName: item.productName,
  productType: item.type?.toString() || 'Material',
  location: item.location,
  currentStock: item.stock?.available,
  minimalOrderQuantity: item.minimalOrderQuantity,
  lastPurchasePrice: item.price?.currentPurchasePrice ? Number(item.price.currentPurchasePrice) : undefined
});

interface MaterialAutocompleteProps {
  value?: MaterialForPurchaseDto | null;
  onSelect: (item: MaterialForPurchaseDto | null) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string;
  className?: string;
}

export const MaterialAutocomplete: React.FC<MaterialAutocompleteProps> = ({
  value,
  onSelect,
  placeholder = "Vyberte položku z katalogu...",
  disabled = false,
  error,
  className = ""
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [searchTerm, setSearchTerm] = useState('');
  const [debouncedSearchTerm, setDebouncedSearchTerm] = useState('');
  const [highlightedIndex, setHighlightedIndex] = useState<number>(-1);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  // Debounce search term
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearchTerm(searchTerm);
    }, 300);

    return () => clearTimeout(timer);
  }, [searchTerm]);

  // Fetch catalog items using autocomplete - filter only Material and Goods for purchases
  const { data: autocompleteData, isLoading, error: fetchError } = useCatalogAutocomplete(
    debouncedSearchTerm || undefined,
    50,
    [ProductType.Material, ProductType.Goods] // Only show materials and goods for purchase orders
  );

  // Extract items directly from autocomplete response using useMemo to stabilize reference
  const items = useMemo(() => autocompleteData?.items || [], [autocompleteData?.items]);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false);
        setHighlightedIndex(-1);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Reset highlighted index when items change
  useEffect(() => {
    setHighlightedIndex(-1);
  }, [items]);

  // Scroll highlighted item into view
  useEffect(() => {
    if (highlightedIndex >= 0 && listRef.current) {
      const items = listRef.current.querySelectorAll('[data-item-index]');
      const highlightedItem = items[highlightedIndex] as HTMLElement;
      if (highlightedItem) {
        highlightedItem.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
    }
  }, [highlightedIndex]);

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = e.target.value;
    setSearchTerm(newValue);
    
    if (!isOpen && newValue.length > 0) {
      setIsOpen(true);
    }
  };

  const handleInputFocus = () => {
    setIsOpen(true);
  };

  const handleSelectItem = useCallback((item: CatalogItemDto) => {
    const material = catalogItemToMaterial(item);
    onSelect(material);
    setSearchTerm(item.productName || '');
    setIsOpen(false);
    setHighlightedIndex(-1);
  }, [onSelect]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') {
        setIsOpen(true);
      }
      return;
    }

    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        setHighlightedIndex(prev => 
          prev < items.length - 1 ? prev + 1 : prev
        );
        break;
        
      case 'ArrowUp':
        e.preventDefault();
        setHighlightedIndex(prev => prev > 0 ? prev - 1 : -1);
        break;
        
      case 'Enter':
        e.preventDefault();
        if (highlightedIndex >= 0 && highlightedIndex < items.length) {
          handleSelectItem(items[highlightedIndex]);
        }
        break;
        
      case 'Escape':
        e.preventDefault();
        setIsOpen(false);
        setHighlightedIndex(-1);
        break;
    }
  }, [isOpen, items, highlightedIndex, handleSelectItem]);

  const handleClear = () => {
    onSelect(null);
    setSearchTerm('');
    setIsOpen(false);
    inputRef.current?.focus();
  };

  const displayValue = value ? value.productName : searchTerm;

  return (
    <div className={`relative ${className}`} ref={dropdownRef}>
      {/* Input */}
      <div className="relative">
        <input
          ref={inputRef}
          type="text"
          value={displayValue}
          onChange={handleInputChange}
          onFocus={handleInputFocus}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          disabled={disabled}
          className={`
            w-full px-3 py-2 pr-10 text-sm border rounded-md transition-colors
            ${error 
              ? 'border-red-300 focus:border-red-500 focus:ring-red-500' 
              : 'border-gray-300 focus:border-indigo-500 focus:ring-indigo-500'
            }
            ${disabled 
              ? 'bg-gray-50 text-gray-500 cursor-not-allowed' 
              : 'bg-white text-gray-900'
            }
            focus:outline-none focus:ring-1
          `}
        />
        
        {/* Clear button or dropdown arrow */}
        <div className="absolute inset-y-0 right-0 flex items-center pr-2">
          {value ? (
            <button
              onClick={handleClear}
              disabled={disabled}
              className="p-1 text-gray-400 hover:text-gray-600 focus:outline-none focus:text-gray-600"
            >
              <Check className="h-4 w-4" />
            </button>
          ) : (
            <ChevronDown
              className={`h-4 w-4 text-gray-400 transition-transform ${
                isOpen ? 'transform rotate-180' : ''
              }`}
            />
          )}
        </div>
      </div>

      {/* Error message */}
      {error && (
        <div className="mt-1 flex items-center text-sm text-red-600">
          <AlertCircle className="h-4 w-4 mr-1" />
          {error}
        </div>
      )}

      {/* Dropdown */}
      {isOpen && (
        <div className="absolute z-50 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg max-h-60 overflow-auto">
          {isLoading && (
            <div className="px-3 py-2 text-sm text-gray-500">
              Načítám...
            </div>
          )}
          
          {fetchError && (
            <div className="px-3 py-2 text-sm text-red-600">
              Chyba při načítání položek katalogu
            </div>
          )}
          
          {items.length > 0 ? (
            <div className="py-1" ref={listRef}>
              {items.map((item, index) => (
                <button
                  key={item.productCode}
                  data-item-index={index}
                  onClick={() => handleSelectItem(item)}
                  onMouseEnter={() => setHighlightedIndex(index)}
                  className={`w-full px-3 py-2 text-left focus:outline-none transition-colors ${
                    index === highlightedIndex 
                      ? 'bg-indigo-100 text-indigo-900' 
                      : 'hover:bg-gray-50'
                  }`}
                >
                  <div className="flex items-center space-x-3">
                    <Package className="h-4 w-4 text-gray-400 flex-shrink-0" />
                    <div className="min-w-0 flex-1">
                      <span className="text-gray-900 truncate">
                        {item.productName} <span className="text-gray-500 font-mono">({item.productCode})</span>
                      </span>
                    </div>
                  </div>
                </button>
              ))}
            </div>
          ) : (
            !isLoading && !fetchError && (
              <div className="px-3 py-2 text-sm text-gray-500">
                {searchTerm ? 'Žádné položky nenalezeny' : 'Začněte psát pro vyhledávání'}
              </div>
            )
          )}
        </div>
      )}
    </div>
  );
};

export default MaterialAutocomplete;