import React, { useId, useRef, useState } from "react";

interface TerminalScanInputProps {
  label: string;
  placeholder?: string;
  onScan: (value: string) => void;
  disabled?: boolean;
  autoFocus?: boolean;
}

const TerminalScanInput: React.FC<TerminalScanInputProps> = ({
  label,
  placeholder,
  onScan,
  disabled = false,
  autoFocus = true,
}) => {
  const [value, setValue] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const inputId = useId();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const trimmed = value.trim();
    if (!trimmed) return;
    onScan(trimmed.toUpperCase());
    setValue("");
    inputRef.current?.focus();
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <label htmlFor={inputId} className="block text-sm font-medium text-neutral-slate">
        {label}
      </label>
      <input
        ref={inputRef}
        id={inputId}
        type="text"
        autoComplete="off"
        autoCapitalize="off"
        autoFocus={autoFocus}
        disabled={disabled}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder={placeholder ?? "Naskenujte kód"}
        data-testid="terminal-scan-input"
        className="w-full px-4 py-3 text-lg font-mono border border-border-light rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue disabled:opacity-50"
      />
      <button
        type="submit"
        disabled={disabled || !value.trim()}
        data-testid="terminal-scan-submit"
        className="w-full py-3 text-base font-semibold text-white bg-primary-blue rounded-xl disabled:opacity-50"
      >
        Potvrdit
      </button>
    </form>
  );
};

export default TerminalScanInput;
