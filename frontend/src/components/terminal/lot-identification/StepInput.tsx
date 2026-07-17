import { useCallback, useEffect, useRef, useState } from 'react';
import { Scan, Loader2 } from 'lucide-react';

interface StepInputProps {
  /** accessible name — the e2e/unit tests target the input by this via role=textbox */
  label: string;
  onScan: (value: string) => void;
  disabled?: boolean;
  loading?: boolean;
  /** prefill (e.g. last-used lot); syncs into the field when it changes and nothing is typed */
  defaultValue?: string;
  placeholder?: string;
  uppercase?: boolean;
  /** focus the field while its step is active */
  active?: boolean;
}

const REFOCUS_DELAY_MS = 100;

/**
 * A plain, form-wrapped capture field for one receive step. A hardware keyboard-wedge scanner
 * types straight into the focused field and appends Enter, which submits the form. Unlike the
 * legacy ScanInput it does NOT fight the shell's singleton wedge for focus — the receive screen
 * yields the wedge (setYieldFocus) while these fields own capture.
 */
const StepInput = ({
  label,
  onScan,
  disabled = false,
  loading = false,
  defaultValue,
  placeholder = 'Naskenujte nebo zadejte kód…',
  uppercase = true,
  active = false,
}: StepInputProps) => {
  const [value, setValue] = useState(defaultValue ?? '');
  const inputRef = useRef<HTMLInputElement>(null);
  const loadingRef = useRef(loading);
  loadingRef.current = loading;
  const hasUserTyped = useRef(false);

  // Keep the field focused while its step is active, and reclaim focus after a save completes.
  useEffect(() => {
    if (active && !disabled) inputRef.current?.focus();
  }, [active, disabled]);

  const prevLoading = useRef(loading);
  useEffect(() => {
    if (prevLoading.current && !loading && active && !disabled) {
      setTimeout(() => inputRef.current?.focus(), REFOCUS_DELAY_MS);
    }
    prevLoading.current = loading;
  }, [loading, active, disabled]);

  // Sync an async prefill (last-used lot) into the field only when the operator hasn't typed yet.
  useEffect(() => {
    if (defaultValue !== undefined && !hasUserTyped.current) setValue(defaultValue);
  }, [defaultValue]);

  const handleChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      hasUserTyped.current = true;
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

  return (
    <form onSubmit={handleSubmit} aria-label={label} className="relative">
      {loading ? (
        <Loader2 className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-neutral-gray dark:text-graphite-muted animate-spin pointer-events-none" />
      ) : (
        <Scan className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-neutral-gray dark:text-graphite-muted pointer-events-none" />
      )}
      <input
        ref={inputRef}
        type="text"
        aria-label={label}
        inputMode="text"
        value={value}
        onChange={handleChange}
        placeholder={placeholder}
        disabled={loading || disabled}
        autoComplete="off"
        autoCapitalize="off"
        className="w-full h-14 pl-10 pr-3 text-lg border border-border-light dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-primary-blue disabled:bg-gray-100 dark:disabled:bg-graphite-surface disabled:cursor-not-allowed"
      />
    </form>
  );
};

export default StepInput;
