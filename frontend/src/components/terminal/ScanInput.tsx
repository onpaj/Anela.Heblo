import React, { useRef, useState, useCallback, useEffect } from 'react';
import { Scan, Loader2 } from 'lucide-react';

interface ScanInputProps {
  label: string;
  placeholder?: string;
  onScan: (value: string) => void;
  loading?: boolean;
  uppercase?: boolean;
  autoFocusOnMount?: boolean;
}

const REFOCUS_DELAY_MS = 100;

const ScanInput: React.FC<ScanInputProps> = ({
  label,
  placeholder = 'Naskenujte nebo zadejte kód...',
  onScan,
  loading = false,
  uppercase = true,
  autoFocusOnMount = true,
}) => {
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);
  const loadingRef = useRef(loading);
  loadingRef.current = loading;

  useEffect(() => {
    if (autoFocusOnMount) {
      inputRef.current?.focus();
    }
    // intentionally runs once on mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setValue(uppercase ? e.target.value.toUpperCase() : e.target.value);
    },
    [uppercase],
  );

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      const trimmed = value.trim();
      if (!trimmed) return;
      onScan(trimmed);
      setValue('');
      setTimeout(() => {
        if (!loadingRef.current) inputRef.current?.focus();
      }, REFOCUS_DELAY_MS);
    },
    [value, onScan],
  );

  const handleBlur = useCallback(() => {
    if (loadingRef.current) return;
    setTimeout(() => {
      if (!loadingRef.current) inputRef.current?.focus();
    }, REFOCUS_DELAY_MS);
  }, []);

  return (
    <div className="space-y-2">
      <label className="block text-sm font-medium text-neutral-slate">{label}</label>
      <form onSubmit={handleSubmit} className="flex gap-2">
        <div className="relative flex-1">
          <Scan className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-neutral-gray pointer-events-none" />
          <input
            ref={inputRef}
            type="text"
            value={value}
            onChange={handleChange}
            onBlur={handleBlur}
            placeholder={placeholder}
            disabled={loading}
            autoComplete="off"
            autoCapitalize="off"
            className="w-full h-14 pl-10 pr-3 text-lg border border-border-light rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-primary-blue disabled:bg-gray-100 disabled:cursor-not-allowed"
          />
        </div>
        <button
          type="submit"
          disabled={loading || !value.trim()}
          className="h-14 px-5 bg-primary-blue text-white font-medium rounded-xl hover:bg-accent-blue-bright disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2 whitespace-nowrap"
        >
          {loading && <Loader2 className="h-5 w-5 animate-spin" />}
          Potvrdit
        </button>
      </form>
    </div>
  );
};

export default ScanInput;
