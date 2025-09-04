import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { ChevronDown, Package, AlertCircle, X } from 'lucide-react';
import { useCatalogAutocomplete } from '../../api/hooks/useCatalogAutocomplete';
import { CatalogItemDto, ProductType } from '../../api/generated/api-client';

// Generic interface for the autocomplete component
interface CatalogAutocompleteProps<T = CatalogItemDto> {
  // Core props
  value?: T | null;
  onSelect: (item: T | null) => void;
  
  // Search and filtering
  placeholder?: string;
  searchMinLength?: number;
  limit?: number;
  productTypes?: ProductType[]; // Optional filtering by product types
  
  // UI customization
  disabled?: boolean;
  error?: string;
  className?: string;
  size?: 'sm' | 'md' | 'lg';
  
  // Behavior
  clearable?: boolean;
  showSelectedInfo?: boolean; // Show selected product info below input
  allowManualEntry?: boolean; // Allow typing custom values (like Journal form)
  
  // Data transformation
  itemAdapter?: (item: CatalogItemDto) => T; // Convert CatalogItemDto to desired type
  displayValue?: (item: T) => string; // How to display selected item
  renderItem?: (item: CatalogItemDto) => React.ReactNode; // Custom item rendering
}

export function CatalogAutocomplete<T = CatalogItemDto>({
  value,
  onSelect,
  placeholder = "Vyberte položku z katalogu...",
  searchMinLength = 2,
  limit = 50,
  productTypes,
  disabled = false,
  error,
  className = "",
  size = 'md',
  clearable = true,
  showSelectedInfo = false,
  allowManualEntry = false,
  itemAdapter,
  displayValue,
  renderItem
}: CatalogAutocompleteProps<T>) {
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

  // Fetch catalog items using autocomplete
  const { data: autocompleteData, isLoading, error: fetchError } = useCatalogAutocomplete(
    debouncedSearchTerm.length >= searchMinLength ? debouncedSearchTerm : undefined,
    limit,
    productTypes
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

  // Sync searchTerm with external value changes (like clearing filters)
  useEffect(() => {
    if (!value) {
      setSearchTerm('');
    } else if (displayValue) {
      setSearchTerm(displayValue(value));
    } else {
      // Default display logic for CatalogItemDto-like objects
      setSearchTerm((value as any).productName || (value as any).productCode || String(value));
    }
  }, [value, displayValue]);

  // Scroll highlighted item into view
  useEffect(() => {
    if (highlightedIndex >= 0 && listRef.current) {
      const listItems = listRef.current.querySelectorAll('[data-item-index]');
      const highlightedItem = listItems[highlightedIndex] as HTMLElement;
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

  const handleInputBlur = () => {
    // In manual entry mode, auto-add product when input loses focus
    if (allowManualEntry && searchTerm.trim()) {
      // Create a minimal item for manual entry
      const manualItem = {
        productCode: searchTerm.trim(),
        productName: searchTerm.trim(),
      } as CatalogItemDto;
      
      const adaptedItem = itemAdapter ? itemAdapter(manualItem) : (manualItem as T);
      onSelect(adaptedItem);
      setSearchTerm('');
    }
  };

  const handleSelectItem = useCallback((item: CatalogItemDto) => {
    const adaptedItem = itemAdapter ? itemAdapter(item) : (item as T);
    onSelect(adaptedItem);
    setSearchTerm(displayValue ? displayValue(adaptedItem) : item.productName || '');
    setIsOpen(false);
    setHighlightedIndex(-1);
  }, [onSelect, itemAdapter, displayValue]);

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
        } else if (allowManualEntry && searchTerm.trim()) {
          // Handle manual entry on Enter
          handleInputBlur();
        }
        break;
        
      case 'Escape':
        e.preventDefault();
        setIsOpen(false);
        setHighlightedIndex(-1);
        break;
    }
  }, [isOpen, items, highlightedIndex, handleSelectItem, allowManualEntry, searchTerm, handleInputBlur]);

  const handleClear = () => {
    onSelect(null);
    setSearchTerm('');
    setIsOpen(false);
    inputRef.current?.focus();
  };

  // Determine display value
  const getDisplayValue = () => {
    if (value) {
      if (displayValue) {
        return displayValue(value);
      }
      // Assume the value has productName if it's CatalogItemDto-like
      return (value as any).productName || (value as any).productCode || String(value);
    }
    return searchTerm;
  };

  // Size-dependent classes
  const sizeClasses = {
    sm: 'px-2 py-1 text-xs',
    md: 'px-3 py-2 text-sm',
    lg: 'px-4 py-3 text-base'
  };

  // Default item renderer
  const defaultRenderItem = (item: CatalogItemDto) => (
    <div className="flex items-center space-x-3">
      <Package className="h-4 w-4 text-gray-400 flex-shrink-0" />
      <div className="min-w-0 flex-1">
        <span className="text-gray-900 truncate">
          {item.productName} <span className="text-gray-500 font-mono">({item.productCode})</span>
        </span>
      </div>
    </div>
  );

  return (
    <div className={`relative ${className}`} ref={dropdownRef}>
      {/* Input */}
      <div className="relative">
        <input
          ref={inputRef}
          type="text"
          value={getDisplayValue()}
          onChange={handleInputChange}
          onFocus={handleInputFocus}
          onBlur={allowManualEntry ? handleInputBlur : undefined}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          disabled={disabled}
          className={`
            w-full pr-10 border rounded-md transition-colors
            ${sizeClasses[size]}
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
          {value && clearable ? (
            <button
              onClick={handleClear}
              disabled={disabled}
              className="p-1 text-gray-400 hover:text-gray-600 focus:outline-none focus:text-gray-600"
            >
              <X className="h-4 w-4" />
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

      {/* Selected product display */}
      {value && showSelectedInfo && (
        <div data-testid="selected-product" className="mt-1 text-sm text-gray-600">
          Selected: {displayValue ? displayValue(value) : (value as any).productName} 
          {(value as any).productCode && ` (${(value as any).productCode})`}
        </div>
      )}

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
                  key={item.productCode || index}
                  data-item-index={index}
                  onClick={() => handleSelectItem(item)}
                  onMouseEnter={() => setHighlightedIndex(index)}
                  className={`w-full px-3 py-2 text-left focus:outline-none transition-colors ${
                    index === highlightedIndex 
                      ? 'bg-indigo-100 text-indigo-900' 
                      : 'hover:bg-gray-50'
                  }`}
                >
                  {renderItem ? renderItem(item) : defaultRenderItem(item)}
                </button>
              ))}
            </div>
          ) : (
            !isLoading && !fetchError && (
              <div className="px-3 py-2 text-sm text-gray-500">
                {searchTerm ? 'Žádné položky nenalezeny' : 
                 allowManualEntry ? 'Začněte psát nebo zadejte vlastní hodnotu' :
                 'Začněte psát pro vyhledávání'}
              </div>
            )
          )}
        </div>
      )}
    </div>
  );
}

export default CatalogAutocomplete;