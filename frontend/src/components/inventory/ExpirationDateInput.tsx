import React, { useEffect, useId, useRef, useState } from "react";
import { Calendar } from "lucide-react";
import { formatCzechDate, formatLocalDate, parseDateInputClamped, parseLocalDate } from "../../utils/dateUtils";

interface ExpirationDateInputProps {
  value: Date | null;
  onChange: (date: Date | null, isValid: boolean) => void;
  readOnly?: boolean;
}

const INPUT_CLASS =
  "w-full text-sm border rounded px-3 py-2 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint";

/**
 * Text date input (d.m.yyyy) with an optional native calendar picker.
 * A native type="date" input silently drops an impossible day such as 31.11.,
 * so typed text is parsed on blur and aligned to the last day of the month.
 */
const ExpirationDateInput: React.FC<ExpirationDateInputProps> = ({ value, onChange, readOnly = false }) => {
  const [text, setText] = useState(value ? formatCzechDate(value) : "");
  const [isInvalid, setIsInvalid] = useState(false);
  const textRef = useRef<HTMLInputElement>(null);
  const pickerRef = useRef<HTMLInputElement>(null);
  const errorId = useId();
  const valueTime = value?.getTime();

  useEffect(() => {
    setText(value ? formatCzechDate(value) : "");
    setIsInvalid(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valueTime]);

  const commitText = () => {
    if (readOnly) return;
    if (text.trim() === "") {
      setIsInvalid(false);
      onChange(null, true);
      return;
    }
    const parsed = parseDateInputClamped(text);
    if (!parsed) {
      setIsInvalid(true);
      onChange(value, false);
      return;
    }
    setIsInvalid(false);
    setText(formatCzechDate(parsed));
    onChange(parsed, true);
  };

  const handlePickerChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (!e.target.value) return;
    const picked = parseLocalDate(e.target.value);
    setIsInvalid(false);
    setText(formatCzechDate(picked));
    onChange(picked, true);
  };

  const openPicker = () => {
    const picker = pickerRef.current;
    if (!picker) return;
    // Clear first so picking the current value still fires onChange and clears an error
    picker.value = "";
    try {
      if (typeof picker.showPicker !== "function") throw new Error("showPicker not supported");
      picker.showPicker();
    } catch {
      textRef.current?.focus();
    }
  };

  const borderClass = isInvalid
    ? "border-red-500 dark:border-red-500"
    : "border-gray-300 dark:border-graphite-border";

  return (
    <div>
      <div className="relative flex items-center">
        <input
          ref={textRef}
          type="text"
          inputMode="numeric"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onBlur={commitText}
          onKeyDown={(e) => {
            if (e.key === "Enter") e.currentTarget.blur();
          }}
          placeholder="dd.mm.rrrr"
          className={`${INPUT_CLASS} ${borderClass} ${readOnly ? "" : "pr-8"}`}
          readOnly={readOnly}
          aria-label="Datum expirace"
          aria-invalid={isInvalid}
          aria-describedby={isInvalid ? errorId : undefined}
          title={readOnly ? "Datum expirace (jen pro čtení)" : "Datum expirace"}
        />
        {!readOnly && (
          <>
            <button
              type="button"
              onClick={openPicker}
              className="absolute right-1 p-1 text-gray-400 hover:text-indigo-600 focus:outline-none dark:text-graphite-faint dark:hover:text-graphite-accent"
              title="Vybrat z kalendáře"
              tabIndex={-1}
            >
              <Calendar className="h-4 w-4" />
            </button>
            <input
              ref={pickerRef}
              type="date"
              value={value ? formatLocalDate(value) : ""}
              onChange={handlePickerChange}
              className="absolute right-0 bottom-0 w-0 h-0 opacity-0 pointer-events-none"
              tabIndex={-1}
              aria-hidden="true"
            />
          </>
        )}
      </div>
      {isInvalid && (
        <p id={errorId} className="mt-1 text-xs text-red-600 dark:text-red-400">
          Neplatné datum (dd.mm.rrrr)
        </p>
      )}
    </div>
  );
};

export default ExpirationDateInput;
