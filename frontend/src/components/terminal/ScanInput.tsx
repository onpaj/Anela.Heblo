import React, { useRef, useState, useCallback, useEffect } from 'react';
import { Scan, Loader2, Keyboard } from 'lucide-react';

interface ScanInputProps {
  label: string;
  placeholder?: string;
  onScan: (value: string) => void;
  loading?: boolean;
  uppercase?: boolean;
  autoFocusOnMount?: boolean;
  refocusOnBlur?: boolean;
  suppressKeyboard?: boolean;
  allowKeyboardToggle?: boolean;
  defaultValue?: string;
}

const REFOCUS_DELAY_MS = 100;

const ScanInput: React.FC<ScanInputProps> = ({
  label,
  placeholder = 'Naskenujte nebo zadejte kód...',
  onScan,
  loading = false,
  uppercase = true,
  autoFocusOnMount = true,
  refocusOnBlur = true,
  suppressKeyboard = false,
  allowKeyboardToggle = false,
  defaultValue,
}) => {
  const [value, setValue] = useState(defaultValue ?? '');
  const [keyboardSuppressed, setKeyboardSuppressed] = useState(suppressKeyboard);
  const inputRef = useRef<HTMLInputElement>(null);
  const loadingRef = useRef(loading);
  loadingRef.current = loading;
  const prevLoadingRef = useRef(loading);
  const refocusOnBlurRef = useRef(refocusOnBlur);
  refocusOnBlurRef.current = refocusOnBlur;

  useEffect(() => {
    if (autoFocusOnMount) {
      inputRef.current?.focus();
    }
    // intentionally runs once on mount
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (defaultValue !== undefined) {
      setValue(defaultValue);
    }
  }, [defaultValue]);

  useEffect(() => {
    if (prevLoadingRef.current && !loading) {
      setTimeout(() => inputRef.current?.focus(), REFOCUS_DELAY_MS);
    }
    prevLoadingRef.current = loading;
  }, [loading]);

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
    if (!refocusOnBlurRef.current || loadingRef.current) return;
    setTimeout(() => {
      if (!loadingRef.current) inputRef.current?.focus();
    }, REFOCUS_DELAY_MS);
  }, []);

  const toggleKeyboard = useCallback(() => {
    setKeyboardSuppressed((prev) => !prev);
    setTimeout(() => inputRef.current?.focus(), REFOCUS_DELAY_MS);
  }, []);

  return (
    <div className="space-y-2">
      <label className="block text-sm font-medium text-neutral-slate dark:text-graphite-text">{label}</label>
      <form onSubmit={handleSubmit} aria-label={label} className="flex gap-2">
        <div className="relative flex-1">
          {loading ? (
            <Loader2 className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-neutral-gray dark:text-graphite-muted animate-spin pointer-events-none" />
          ) : (
            <Scan className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-neutral-gray dark:text-graphite-muted pointer-events-none" />
          )}
          <input
            ref={inputRef}
            type="text"
            inputMode={keyboardSuppressed ? 'none' : 'text'}
            value={value}
            onChange={handleChange}
            onBlur={handleBlur}
            placeholder={placeholder}
            disabled={loading}
            autoComplete="off"
            autoCapitalize="off"
            className="w-full h-14 pl-10 pr-3 text-lg border border-border-light dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-primary-blue disabled:bg-gray-100 dark:disabled:bg-graphite-surface disabled:cursor-not-allowed"
          />
        </div>
        {allowKeyboardToggle && (
          <button
            type="button"
            onClick={toggleKeyboard}
            aria-label={keyboardSuppressed ? 'Zobrazit klávesnici' : 'Skrýt klávesnici'}
            aria-pressed={!keyboardSuppressed}
            className={`h-14 px-3 rounded-xl border transition-colors flex items-center justify-center ${
              keyboardSuppressed
                ? 'border-border-light dark:border-graphite-border text-neutral-gray dark:text-graphite-muted hover:text-primary-blue dark:hover:text-graphite-accent hover:border-primary-blue dark:hover:border-graphite-accent'
                : 'border-primary-blue dark:border-graphite-accent text-primary-blue dark:text-graphite-accent bg-secondary-blue-pale dark:bg-graphite-surface-2'
            }`}
          >
            <Keyboard className="h-5 w-5" />
          </button>
        )}
      </form>
    </div>
  );
};

export default ScanInput;
