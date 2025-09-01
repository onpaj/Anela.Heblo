import React, { useState, useRef, useEffect } from 'react';
import { ChevronDown, Loader2, Building2 } from 'lucide-react';
import { useSupplierSearch } from '../../api/hooks/useSuppliers';
import { SupplierDto } from '../../api/generated/api-client';

interface SupplierAutocompleteProps {
  value: SupplierDto | null;
  onSelect: (supplier: SupplierDto | null) => void;
  placeholder?: string;
  error?: string;
  className?: string;
  disabled?: boolean;
}

const SupplierAutocomplete: React.FC<SupplierAutocompleteProps> = ({
  value,
  onSelect,
  placeholder = "Vyberte dodavatele...",
  error,
  className = "",
  disabled = false
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  const [isOpen, setIsOpen] = useState(false);
  const [inputValue, setInputValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const { suppliers, isLoading } = useSupplierSearch(searchTerm);

  // Update input value when value prop changes
  useEffect(() => {
    if (value && value.name) {
      setInputValue(value.name);
      setSearchTerm('');
    } else {
      setInputValue('');
    }
  }, [value]);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (
        dropdownRef.current && 
        !dropdownRef.current.contains(event.target as Node) &&
        inputRef.current &&
        !inputRef.current.contains(event.target as Node)
      ) {
        setIsOpen(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = e.target.value;
    setInputValue(newValue);
    setSearchTerm(newValue);
    setIsOpen(true);

    // Clear selection if input is cleared
    if (!newValue && value) {
      onSelect(null);
    }
  };

  const handleInputFocus = () => {
    setIsOpen(true);
    if (!searchTerm && inputValue) {
      setSearchTerm(inputValue);
    }
  };

  const handleSupplierSelect = (supplier: SupplierDto) => {
    onSelect(supplier);
    setInputValue(supplier.name || '');
    setSearchTerm('');
    setIsOpen(false);
    inputRef.current?.blur();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setIsOpen(false);
      inputRef.current?.blur();
    } else if (e.key === 'ArrowDown' && suppliers.length > 0) {
      e.preventDefault();
      setIsOpen(true);
      // Focus first option
      const firstOption = dropdownRef.current?.querySelector('[role="option"]') as HTMLElement;
      firstOption?.focus();
    }
  };

  const handleOptionKeyDown = (e: React.KeyboardEvent, supplier: SupplierDto, index: number) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      handleSupplierSelect(supplier);
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      const nextOption = dropdownRef.current?.querySelectorAll('[role="option"]')[index + 1] as HTMLElement;
      nextOption?.focus();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      if (index === 0) {
        inputRef.current?.focus();
      } else {
        const prevOption = dropdownRef.current?.querySelectorAll('[role="option"]')[index - 1] as HTMLElement;
        prevOption?.focus();
      }
    } else if (e.key === 'Escape') {
      setIsOpen(false);
      inputRef.current?.focus();
    }
  };

  return (
    <div className={`relative ${className}`}>
      <div className="relative">
        <input
          ref={inputRef}
          type="text"
          value={inputValue}
          onChange={handleInputChange}
          onFocus={handleInputFocus}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          disabled={disabled}
          className={`block w-full px-3 py-1.5 pr-8 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500 ${
            error ? 'border-red-300' : 'border-gray-300'
          } ${disabled ? 'bg-gray-100 cursor-not-allowed' : 'bg-white'}`}
          autoComplete="off"
          role="combobox"
          aria-expanded={isOpen}
          aria-haspopup="listbox"
          aria-controls={isOpen ? "supplier-listbox" : undefined}
        />
        <div className="absolute inset-y-0 right-0 flex items-center pr-2">
          {isLoading ? (
            <Loader2 className="h-4 w-4 text-gray-400 animate-spin" />
          ) : (
            <ChevronDown className="h-4 w-4 text-gray-400" />
          )}
        </div>
      </div>

      {error && (
        <p className="mt-1 text-sm text-red-600">{error}</p>
      )}

      {isOpen && (searchTerm || suppliers.length > 0) && (
        <div
          ref={dropdownRef}
          id="supplier-listbox"
          className="absolute z-10 w-full mt-1 bg-white border border-gray-300 rounded-md shadow-lg max-h-60 overflow-y-auto"
          role="listbox"
        >
          {isLoading && searchTerm ? (
            <div className="px-3 py-2 text-sm text-gray-500 flex items-center">
              <Loader2 className="h-4 w-4 mr-2 animate-spin" />
              Hledám dodavatele...
            </div>
          ) : suppliers.length > 0 ? (
            suppliers.map((supplier, index) => (
              <div
                key={supplier.id}
                role="option"
                tabIndex={-1}
                className="px-3 py-2 cursor-pointer hover:bg-indigo-50 focus:bg-indigo-50 focus:outline-none"
                onClick={() => handleSupplierSelect(supplier)}
                onKeyDown={(e) => handleOptionKeyDown(e, supplier, index)}
                aria-selected={false}
              >
                <div className="flex items-start">
                  <Building2 className="h-4 w-4 text-gray-400 mt-0.5 mr-2 flex-shrink-0" />
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-medium text-gray-900 truncate">
                        {supplier.name || 'N/A'}
                      </span>
                      <span className="text-xs text-gray-500 ml-2 flex-shrink-0">
                        {supplier.code || 'N/A'}
                      </span>
                    </div>
                    {(supplier.email || supplier.phone) && (
                      <div className="text-xs text-gray-500 mt-0.5">
                        {supplier.email && (
                          <span className="mr-3">{supplier.email}</span>
                        )}
                        {supplier.phone && (
                          <span>{supplier.phone}</span>
                        )}
                      </div>
                    )}
                    {supplier.note && (
                      <div className="text-xs text-gray-400 mt-0.5 truncate">
                        {supplier.note}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            ))
          ) : searchTerm && searchTerm.length >= 2 ? (
            <div className="px-3 py-2 text-sm text-gray-500">
              Žádní dodavatelé nenalezeni pro "{searchTerm}"
            </div>
          ) : (
            <div className="px-3 py-2 text-sm text-gray-500">
              Začněte psát pro vyhledání dodavatelů...
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default SupplierAutocomplete;