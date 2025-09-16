import React, { useState, useMemo } from "react";
import Select, {
  StylesConfig,
  components,
  OptionProps,
  SingleValueProps,
  SingleValue,
  ActionMeta,
} from "react-select";
import { User, AlertCircle, ChevronDown } from "lucide-react";
import { useResponsiblePersonsQuery } from "../../api/hooks/useUserManagement";

interface ResponsiblePersonComboboxProps {
  value?: string | null;
  onChange: (value: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  error?: string;
  className?: string;
  allowManualEntry?: boolean; // Allow typing custom values as fallback
}

interface ResponsiblePersonSelectOption {
  value: string;
  label: string;
  displayName: string;
  email: string;
  isCustom?: boolean; // Indicates manually entered value
}

const ResponsiblePersonCombobox: React.FC<ResponsiblePersonComboboxProps> = ({
  value,
  onChange,
  placeholder = "Select responsible person...",
  disabled = false,
  error,
  className = "",
  allowManualEntry = true,
}) => {
  const [inputValue, setInputValue] = useState("");
  const { data: response, isLoading, isError } = useResponsiblePersonsQuery();

  const options = useMemo((): ResponsiblePersonSelectOption[] => {
    const members = response?.members || [];
    const memberOptions: ResponsiblePersonSelectOption[] = members.map((member) => ({
      value: member.displayName,
      label: `${member.displayName} (${member.email})`,
      displayName: member.displayName,
      email: member.email,
    }));

    // If manual entry is allowed and there's an input value that doesn't match any member
    if (allowManualEntry && inputValue && !memberOptions.some(opt => 
      opt.displayName.toLowerCase().includes(inputValue.toLowerCase()) ||
      opt.email.toLowerCase().includes(inputValue.toLowerCase())
    )) {
      memberOptions.push({
        value: inputValue,
        label: `${inputValue} (custom entry)`,
        displayName: inputValue,
        email: "",
        isCustom: true,
      });
    }

    return memberOptions;
  }, [response?.members, inputValue, allowManualEntry]);

  const selectedOption = useMemo(() => {
    if (!value) return null;
    
    // First try to find an exact match in the options
    const exactMatch = options.find(opt => opt.value === value);
    if (exactMatch) return exactMatch;
    
    // If no exact match and manual entry is allowed, create a custom option
    if (allowManualEntry) {
      return {
        value: value,
        label: `${value} (custom entry)`,
        displayName: value,
        email: "",
        isCustom: true,
      };
    }
    
    return null;
  }, [value, options, allowManualEntry]);

  const handleChange = (
    newValue: SingleValue<ResponsiblePersonSelectOption>,
    actionMeta: ActionMeta<ResponsiblePersonSelectOption>
  ) => {
    onChange(newValue ? newValue.value : null);
  };

  const handleInputChange = (newValue: string) => {
    setInputValue(newValue);
    return newValue;
  };

  // Custom components
  const CustomOption = (props: OptionProps<ResponsiblePersonSelectOption>) => {
    const { data } = props;
    return (
      <components.Option {...props}>
        <div className="flex items-center space-x-3">
          <User className="h-4 w-4 text-gray-400" />
          <div className="flex-1">
            <div className="font-medium text-gray-900">{data.displayName}</div>
            {data.email && (
              <div className="text-sm text-gray-500">{data.email}</div>
            )}
            {data.isCustom && (
              <div className="text-xs text-blue-600">Custom entry</div>
            )}
          </div>
        </div>
      </components.Option>
    );
  };

  const CustomSingleValue = (props: SingleValueProps<ResponsiblePersonSelectOption>) => {
    const { data } = props;
    return (
      <components.SingleValue {...props}>
        <div className="flex items-center space-x-2">
          <User className="h-4 w-4 text-gray-400" />
          <span>{data.displayName}</span>
          {data.isCustom && (
            <span className="text-xs text-blue-600">(custom)</span>
          )}
        </div>
      </components.SingleValue>
    );
  };

  const DropdownIndicator = (props: any) => (
    <components.DropdownIndicator {...props}>
      <ChevronDown className="h-4 w-4 text-gray-400" />
    </components.DropdownIndicator>
  );

  // Styles for react-select
  const customStyles: StylesConfig<ResponsiblePersonSelectOption> = {
    control: (provided, state) => ({
      ...provided,
      borderColor: error ? '#ef4444' : state.isFocused ? '#3b82f6' : '#d1d5db',
      boxShadow: state.isFocused ? '0 0 0 1px #3b82f6' : 'none',
      '&:hover': {
        borderColor: error ? '#ef4444' : state.isFocused ? '#3b82f6' : '#9ca3af',
      },
      minHeight: '38px',
    }),
    option: (provided, state) => ({
      ...provided,
      backgroundColor: state.isSelected
        ? '#3b82f6'
        : state.isFocused
        ? '#f3f4f6'
        : 'white',
      color: state.isSelected ? 'white' : '#1f2937',
      padding: '8px 12px',
    }),
    menu: (provided) => ({
      ...provided,
      zIndex: 50000,
    }),
    menuPortal: (provided) => ({
      ...provided,
      zIndex: 50000,
    }),
    noOptionsMessage: (provided) => ({
      ...provided,
      color: '#6b7280',
      fontSize: '14px',
    }),
  };

  return (
    <div className={`relative ${className}`}>
      <Select<ResponsiblePersonSelectOption>
        value={selectedOption}
        onChange={handleChange}
        onInputChange={handleInputChange}
        options={options}
        components={{
          Option: CustomOption,
          SingleValue: CustomSingleValue,
          DropdownIndicator,
        }}
        styles={customStyles}
        placeholder={placeholder}
        isDisabled={disabled}
        isLoading={isLoading}
        isClearable
        isSearchable
        menuPortalTarget={document.body}
        menuPosition="fixed"
        menuShouldBlockScroll={false}
        closeMenuOnScroll={false}
        noOptionsMessage={() => {
          if (isError) {
            return "Failed to load team members";
          }
          if (isLoading) {
            return "Loading team members...";
          }
          if (allowManualEntry && inputValue) {
            return `Press Enter to add "${inputValue}" as custom entry`;
          }
          return "No team members found";
        }}
      />
      
      {isError && (
        <div className="mt-1 flex items-center space-x-1 text-sm text-amber-600">
          <AlertCircle className="h-4 w-4" />
          <span>Could not load team members. You can still enter names manually.</span>
        </div>
      )}
      
      {error && (
        <div className="mt-1 flex items-center space-x-1 text-sm text-red-600">
          <AlertCircle className="h-4 w-4" />
          <span>{error}</span>
        </div>
      )}
    </div>
  );
};

export default ResponsiblePersonCombobox;