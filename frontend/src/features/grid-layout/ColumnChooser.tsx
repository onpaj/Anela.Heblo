import React, { useState } from 'react';
import { Settings2 } from 'lucide-react';
import { GridColumn, GridColumnState } from './types';

interface ColumnChooserProps<TRow> {
  columns: GridColumn<TRow>[];
  columnState: GridColumnState[];
  onToggle: (id: string) => void;
  onReset: () => void;
}

export function ColumnChooser<TRow>({ columns, columnState, onToggle, onReset }: ColumnChooserProps<TRow>) {
  const [open, setOpen] = useState(false);

  const hidableColumns = columns.filter((c) => c.canHide !== false);

  const isHidden = (id: string) =>
    columnState.find((s) => s.id === id)?.hidden ?? false;

  return (
    <div className="relative">
      <button
        type="button"
        className="flex items-center gap-1 px-3 py-2 text-sm text-gray-600 border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500"
        onClick={() => setOpen((v) => !v)}
        aria-label="Sloupce"
      >
        <Settings2 className="h-4 w-4" />
        <span>Sloupce</span>
      </button>

      {open && (
        <>
          <div
            className="fixed inset-0 z-20"
            onClick={() => setOpen(false)}
          />
          <div className="absolute right-0 z-30 mt-1 w-52 bg-white border border-gray-200 rounded-md shadow-lg">
            <div className="p-3 space-y-2 max-h-72 overflow-y-auto">
              {hidableColumns.map((col) => {
                const inputId = `col-chooser-${col.id}`;
                return (
                  <label
                    key={col.id}
                    htmlFor={inputId}
                    className="flex items-center gap-2 cursor-pointer text-sm text-gray-700 hover:text-gray-900"
                  >
                    <input
                      id={inputId}
                      type="checkbox"
                      className="h-4 w-4 text-indigo-600 rounded border-gray-300 focus:ring-indigo-500"
                      checked={!isHidden(col.id)}
                      onChange={() => onToggle(col.id)}
                      aria-label={typeof col.header === 'string' ? col.header : col.id}
                    />
                    {col.header}
                  </label>
                );
              })}
            </div>
            <div className="border-t border-gray-100 p-2">
              <button
                type="button"
                className="w-full text-sm text-gray-500 hover:text-gray-700 px-2 py-1 rounded hover:bg-gray-50"
                onClick={() => { onReset(); setOpen(false); }}
              >
                Reset rozvržení
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
