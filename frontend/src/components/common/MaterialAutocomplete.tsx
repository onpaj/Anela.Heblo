import React, { useState, useEffect, useRef, useCallback } from 'react';
import { ChevronDown, Check, Package, AlertCircle } from 'lucide-react';
import { useMaterialsForPurchase, MaterialForPurchaseDto } from '../../api/hooks/useMaterials';

interface MaterialAutocompleteProps {
  value?: MaterialForPurchaseDto | null;
  onSelect: (material: MaterialForPurchaseDto | null) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string;
  className?: string;
}

export const MaterialAutocomplete: React.FC<MaterialAutocompleteProps> = ({
  value,
  onSelect,
  placeholder = "Vyberte materiál nebo zboží...",
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

  // Fetch materials based on debounced search term
  const { data: materials, isLoading, error: fetchError } = useMaterialsForPurchase(
    debouncedSearchTerm || undefined,
    50
  );

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

  // Reset highlighted index when materials change
  useEffect(() => {
    setHighlightedIndex(-1);
  }, [materials]);

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

  const handleSelectMaterial = useCallback((material: MaterialForPurchaseDto) => {
    onSelect(material);
    setSearchTerm(material.productName || '');
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

    const materialsList = materials?.materials || [];
    
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault();
        setHighlightedIndex(prev => 
          prev < materialsList.length - 1 ? prev + 1 : prev
        );
        break;
        
      case 'ArrowUp':
        e.preventDefault();
        setHighlightedIndex(prev => prev > 0 ? prev - 1 : -1);
        break;
        
      case 'Enter':
        e.preventDefault();
        if (highlightedIndex >= 0 && highlightedIndex < materialsList.length) {
          handleSelectMaterial(materialsList[highlightedIndex]);
        }
        break;
        
      case 'Escape':
        e.preventDefault();
        setIsOpen(false);
        setHighlightedIndex(-1);
        break;
    }
  }, [isOpen, materials, highlightedIndex, handleSelectMaterial]);

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
              Chyba při načítání materiálů
            </div>
          )}
          
          {materials && materials.materials && materials.materials.length > 0 ? (
            <div className="py-1" ref={listRef}>
              {materials.materials.map((material, index) => (
                <button
                  key={material.productCode}
                  data-item-index={index}
                  onClick={() => handleSelectMaterial(material)}
                  onMouseEnter={() => setHighlightedIndex(index)}
                  className={`w-full px-3 py-2 text-left focus:outline-none transition-colors ${
                    index === highlightedIndex 
                      ? 'bg-indigo-100 text-indigo-900' 
                      : 'hover:bg-gray-50'
                  }`}
                >
                  <div className="flex items-start space-x-3">
                    <Package className="h-4 w-4 text-gray-400 mt-0.5 flex-shrink-0" />
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center space-x-2">
                        <span className="font-medium text-gray-900 truncate">
                          {material.productName}
                        </span>
                        <span className="text-xs text-gray-500 bg-gray-100 px-2 py-0.5 rounded">
                          {material.productType}
                        </span>
                      </div>
                      <div className="text-sm text-gray-500 mt-0.5">
                        <span className="font-mono">{material.productCode}</span>
                        {material.location && (
                          <>
                            {' • '}
                            <span>{material.location}</span>
                          </>
                        )}
                        {material.currentStock !== undefined && (
                          <>
                            {' • '}
                            <span>Sklad: {material.currentStock}</span>
                          </>
                        )}
                        {material.minimalOrderQuantity && (
                          <>
                            {' • '}
                            <span>MOQ: {material.minimalOrderQuantity}</span>
                          </>
                        )}
                        {material.lastPurchasePrice && (
                          <>
                            {' • '}
                            <span>Posl. cena: {material.lastPurchasePrice.toFixed(2)} Kč</span>
                          </>
                        )}
                      </div>
                    </div>
                  </div>
                </button>
              ))}
            </div>
          ) : (
            !isLoading && !fetchError && (
              <div className="px-3 py-2 text-sm text-gray-500">
                {searchTerm ? 'Žádné materiály nenalezeny' : 'Začněte psát pro vyhledávání'}
              </div>
            )
          )}
        </div>
      )}
    </div>
  );
};