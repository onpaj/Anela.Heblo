import React, { useEffect, useRef, useState } from "react";
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
  const pickerRef = useRef<HTMLInputElement>(null);
  const valueTime = value?.getTime();

  useEffect(() => {
    setText(value ? formatCzechDate(value) : "");
    setIsInvalid(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valueTime]);

  const commitText = () => {
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
    if (e.target.value) onChange(parseLocalDate(e.target.value), true);
  };

  const borderClass = isInvalid
    ? "border-red-500 dark:border-red-500"
    : "border-gray-300 dark:border-graphite-border";

  return (
    <div className="relative flex items-center">
      <input
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
        aria-invalid={isInvalid}
        title={readOnly ? "Datum expirace (jen pro čtení)" : isInvalid ? "Neplatné datum – zadejte dd.mm.rrrr" : "Datum expirace"}
      />
      {!readOnly && (
        <>
          <button
            type="button"
            onClick={() => pickerRef.current?.showPicker?.()}
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
  );
};

export default ExpirationDateInput;
