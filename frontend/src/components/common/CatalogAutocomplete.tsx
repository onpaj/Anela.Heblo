import React, { useState, useEffect, useMemo } from "react";
import Select, {
  StylesConfig,
  components,
  OptionProps,
  SingleValueProps,
  SingleValue,
  MultiValue,
  ActionMeta,
} from "react-select";
import { Package, AlertCircle } from "lucide-react";
import { useCatalogAutocomplete } from "../../api/hooks/useCatalogAutocomplete";
import { CatalogItemDto, ProductType } from "../../api/generated/api-client";

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
  size?: "sm" | "md" | "lg";

  // Behavior
  clearable?: boolean;
  showSelectedInfo?: boolean; // Show selected product info below input
  allowManualEntry?: boolean; // Allow typing custom values (like Journal form)

  // Data transformation
  itemAdapter?: (item: CatalogItemDto) => T; // Convert CatalogItemDto to desired type
  displayValue?: (item: T) => string; // How to display selected item
  renderItem?: (item: CatalogItemDto) => React.ReactNode; // Custom item rendering
}

// React Select Option type for our catalog items
interface CatalogSelectOption {
  value: string;
  label: string;
  productCode?: string;
  productName?: string;
  type?: ProductType;
  data?: CatalogItemDto; // Complete catalog item data for preserving all fields
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
  size = "md",
  clearable = true,
  showSelectedInfo = false,
  allowManualEntry = false,
  itemAdapter,
  displayValue,
  renderItem,
}: CatalogAutocompleteProps<T>) {
  // Convert CatalogItemDto to Select option format
  const convertToOption = (item: CatalogItemDto): CatalogSelectOption => ({
    value: item.productCode || "",
    label: `${item.productName} (${item.productCode})`,
    productCode: item.productCode,
    productName: item.productName,
    type: item.type,
    data: item, // Store complete catalog item data
  });

  // Convert current value to select option
  const getSelectValue = (): CatalogSelectOption | null => {
    if (!value) return null;

    // If value is already a CatalogItemDto-like object
    const catalogItem = value as any;
    const displayName =
      catalogItem.productName ||
      catalogItem.label ||
      (displayValue ? displayValue(value) : String(value));
    const code = catalogItem.productCode || catalogItem.value || "";

    return {
      productCode: code,
      productName: displayName,
      value: code,
      label: catalogItem.productName
        ? `${catalogItem.productName} (${catalogItem.productCode})`
        : displayName,
    } as CatalogSelectOption;
  };

  // State for search input with debouncing
  const [searchTerm, setSearchTerm] = useState("");
  const [debouncedSearchTerm, setDebouncedSearchTerm] = useState("");

  // Debounce search term
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearchTerm(searchTerm);
    }, 300);

    return () => clearTimeout(timer);
  }, [searchTerm]);

  // Use existing hook for data fetching
  const { data: autocompleteData, isLoading } = useCatalogAutocomplete(
    debouncedSearchTerm.length >= searchMinLength
      ? debouncedSearchTerm
      : undefined,
    limit,
    productTypes,
  );

  // Convert hook data to options
  const options = useMemo(() => {
    if (!autocompleteData?.items) return [];
    return autocompleteData.items.map(convertToOption);
  }, [autocompleteData?.items]);

  // Handle input change for search
  const handleInputChange = (inputValue: string) => {
    setSearchTerm(inputValue);
  };

  // Handle selection change
  const handleChange = (
    newValue:
      | SingleValue<CatalogSelectOption>
      | MultiValue<CatalogSelectOption>,
    actionMeta: ActionMeta<CatalogSelectOption>,
  ) => {
    const selectedOption = newValue as SingleValue<CatalogSelectOption>;

    if (!selectedOption) {
      onSelect(null);
      return;
    }

    // Use complete catalog item data if available, otherwise create new instance
    const catalogItem = selectedOption.data || new CatalogItemDto({
      productCode: selectedOption.productCode,
      productName: selectedOption.productName,
      type: selectedOption.type,
    });

    const adaptedItem = itemAdapter
      ? itemAdapter(catalogItem)
      : (catalogItem as T);
    onSelect(adaptedItem);
  };

  // Size-dependent styles
  const getSizeStyles = () => {
    switch (size) {
      case "sm":
        return {
          control: (base: any) => ({
            ...base,
            minHeight: "32px",
            height: "34px",
            fontSize: "12px",
          }),
          valueContainer: (base: any) => ({ ...base, padding: "5px 8px" }),
          input: (base: any) => ({ ...base, margin: "0px" }),
        };
      case "lg":
        return {
          control: (base: any) => ({
            ...base,
            minHeight: "48px",
            fontSize: "16px",
          }),
          valueContainer: (base: any) => ({ ...base, padding: "12px 16px" }),
          input: (base: any) => ({ ...base, margin: "0px" }),
        };
      default: // md
        return {
          control: (base: any) => ({
            ...base,
            minHeight: "32px",
            height: "34px",
            fontSize: "14px",
          }),
          valueContainer: (base: any) => ({ ...base, padding: "5px 12px" }),
          input: (base: any) => ({ ...base, margin: "0px" }),
        };
    }
  };

  // Custom styles to match Tailwind design
  const customStyles: StylesConfig<CatalogSelectOption> = {
    ...getSizeStyles(),
    control: (base, state) => ({
      ...base,
      ...getSizeStyles().control(base),
      borderColor: error ? "#fca5a5" : state.isFocused ? "#6366f1" : "#d1d5db",
      borderRadius: "6px",
      borderWidth: "1px",
      boxShadow: state.isFocused
        ? error
          ? "0 0 0 1px #ef4444"
          : "0 0 0 1px #6366f1"
        : "none",
      backgroundColor: disabled ? "#f9fafb" : "white",
      cursor: disabled ? "not-allowed" : "default",
      "&:hover": {
        borderColor: error
          ? "#fca5a5"
          : state.isFocused
            ? "#6366f1"
            : "#9ca3af",
      },
    }),
    option: (base, state) => ({
      ...base,
      backgroundColor: state.isSelected
        ? "#6366f1"
        : state.isFocused
          ? "#e0e7ff"
          : "white",
      color: state.isSelected
        ? "white"
        : state.isFocused
          ? "#312e81"
          : "#111827",
      cursor: "pointer",
      padding: "8px 12px",
    }),
    menu: (base) => ({
      ...base,
      borderRadius: "6px",
      border: "1px solid #d1d5db",
      boxShadow:
        "0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)",
      zIndex: 9999999, // Extremely high to ensure it appears above sticky table headers
      minWidth: "350px", // Reasonable minimum width
      maxWidth: "90vw", // Use viewport width to prevent overflow
      width: "max-content", // Size based on content
    }),
    menuPortal: (base) => ({
      ...base,
      zIndex: 9999999, // Ensure portal also has proper z-index
    }),
    menuList: (base) => ({
      ...base,
      maxHeight: "240px",
      padding: "4px 0",
    }),
    placeholder: (base) => ({
      ...base,
      color: "#6b7280",
    }),
    singleValue: (base) => ({
      ...base,
      color: disabled ? "#6b7280" : "#111827",
    }),
    clearIndicator: (base) => ({
      ...base,
      color: "#9ca3af",
      cursor: "pointer",
      "&:hover": {
        color: "#6b7280",
      },
    }),
    dropdownIndicator: (base) => ({
      ...base,
      color: "#9ca3af",
      "&:hover": {
        color: "#6b7280",
      },
    }),
  };

  // Custom Option component with icon
  const CustomOption = (props: OptionProps<CatalogSelectOption>) => {
    // Use complete catalog item data if available, otherwise create new instance
    const catalogItem = props.data.data || new CatalogItemDto({
      productCode: props.data.productCode,
      productName: props.data.productName,
      type: props.data.type,
    });

    return (
      <components.Option {...props}>
        <div className="flex items-center space-x-3">
          <Package className="h-4 w-4 text-gray-400 flex-shrink-0" />
          <div className="min-w-0 flex-1">
            {renderItem ? (
              renderItem(catalogItem)
            ) : (
              <span className="text-gray-900 truncate">
                {props.data.productName}{" "}
                <span className="text-gray-500 font-mono">
                  ({props.data.productCode})
                </span>
              </span>
            )}
          </div>
        </div>
      </components.Option>
    );
  };

  // Custom SingleValue component
  const CustomSingleValue = (props: SingleValueProps<CatalogSelectOption>) => {
    return (
      <components.SingleValue {...props}>
        <div className="flex items-center space-x-2">
          <Package className="h-4 w-4 text-gray-400 flex-shrink-0" />
          <span className="truncate">
            {displayValue && value
              ? displayValue(value)
              : props.data.productName}
          </span>
        </div>
      </components.SingleValue>
    );
  };

  return (
    <div className={className}>
      <Select
        value={getSelectValue()}
        onChange={handleChange}
        options={options}
        placeholder={placeholder}
        isClearable={clearable}
        isDisabled={disabled}
        isSearchable={true}
        isLoading={isLoading}
        onInputChange={handleInputChange}
        styles={customStyles}
        components={{
          Option: CustomOption,
          SingleValue: CustomSingleValue,
        }}
        noOptionsMessage={({ inputValue }) =>
          inputValue
            ? "Žádné položky nenalezeny"
            : allowManualEntry
              ? "Začněte psát nebo zadejte vlastní hodnotu"
              : "Začněte psát pro vyhledávání"
        }
        loadingMessage={() => "Načítám..."}
        menuPlacement="auto"
        menuPosition="absolute"
        menuShouldScrollIntoView={false}
        filterOption={null} // Disable built-in filtering since we handle it via API
      />

      {/* Selected product display */}
      {value && showSelectedInfo && (
        <div
          data-testid="selected-product"
          className="mt-1 text-sm text-gray-600"
        >
          Selected:{" "}
          {displayValue ? displayValue(value) : (value as any).productName}
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
    </div>
  );
}

export default CatalogAutocomplete;
