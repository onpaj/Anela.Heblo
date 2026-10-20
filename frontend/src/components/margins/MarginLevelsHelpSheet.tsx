import React, { useCallback, useEffect, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { X, Loader2, AlertCircle } from "lucide-react";

/**
 * Definice hladin marže, servírovaná jako statický dokument.
 *
 * Dokument je jediný zdroj pravdy (frontend/public/docs/margin-levels.md) —
 * načítá se za běhu, aby nebylo nutné držet druhou kopii v kódu, která by se
 * rozešla s tou v repozitáři. Nápověda, která lže, je horší než žádná.
 */
export const MARGIN_LEVELS_DOC_URL = `${process.env.PUBLIC_URL ?? ""}/docs/margin-levels.md`;

/**
 * SPA fallback vrací index.html se statusem 200 pro každou neznámou cestu, takže
 * samotné `response.ok` chybějící dokument nepozná — bez téhle kontroly by se
 * uživateli vykreslil zdroják index.html místo nápovědy.
 */
const ACCEPTED_CONTENT_TYPES = ["text/markdown", "text/plain"];

export interface MarginLevelsHelpSheetProps {
  onClose: () => void;
}

const MarginLevelsHelpSheet: React.FC<MarginLevelsHelpSheetProps> = ({ onClose }) => {
  const [markdown, setMarkdown] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const isBackdropPressed = useRef(false);

  useEffect(() => {
    let isActive = true;

    const load = async () => {
      try {
        const response = await fetch(MARGIN_LEVELS_DOC_URL);
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }

        const contentType = response.headers?.get("content-type") ?? "";
        const isMarkdown = ACCEPTED_CONTENT_TYPES.some((type) => contentType.startsWith(type));
        if (!isMarkdown) {
          throw new Error(`Unexpected content-type: ${contentType || "(none)"}`);
        }

        const text = await response.text();
        if (isActive) {
          setMarkdown(text);
        }
      } catch (e) {
        console.error("Nepodařilo se načíst nápovědu k hladinám marže", MARGIN_LEVELS_DOC_URL, e);
        if (isActive) {
          setError("Dokumentaci se nepodařilo načíst.");
        }
      }
    };

    load();
    return () => {
      isActive = false;
    };
  }, []);

  const handleKeyDown = useCallback(
    (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        onClose();
      }
    },
    [onClose],
  );

  useEffect(() => {
    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [handleKeyDown]);

  // Dokument je delší než okno, takže se scrolluje. Bez přesunu fokusu dovnitř
  // by se klávesnicí nedal přečíst — fokus by zůstal na otazníku pod overlayem.
  useEffect(() => {
    const trigger = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    return () => trigger?.focus?.();
  }, []);

  // Zavírá se až na mouseup, a jen když obě události patří pozadí. Klik na
  // `onClick` samotný zavírá i tažení myší při označování textu, které skončí
  // mimo dialog — a z tohohle dokumentu si lidé text kopírují.
  const handleBackdropMouseDown = (event: React.MouseEvent<HTMLDivElement>) => {
    isBackdropPressed.current = event.target === event.currentTarget;
  };

  const handleBackdropMouseUp = (event: React.MouseEvent<HTMLDivElement>) => {
    if (isBackdropPressed.current && event.target === event.currentTarget) {
      onClose();
    }
    isBackdropPressed.current = false;
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      onMouseDown={handleBackdropMouseDown}
      onMouseUp={handleBackdropMouseUp}
      data-testid="margin-levels-help-sheet"
    >
      <div
        ref={dialogRef}
        tabIndex={-1}
        className="flex max-h-[85%] w-full max-w-3xl flex-col overflow-hidden rounded-xl bg-white shadow-lg outline-none dark:bg-graphite-surface dark:shadow-soft-dark"
        role="dialog"
        aria-modal="true"
        aria-label="Hladiny marže"
      >
        <div className="flex items-center justify-between border-b border-gray-200 px-5 py-3 dark:border-graphite-border">
          <h2 className="text-sm font-semibold dark:text-graphite-text">Hladiny marže</h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Zavřít"
            className="text-gray-400 hover:text-gray-600 dark:text-graphite-faint dark:hover:text-graphite-muted"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="overflow-y-auto px-6 py-5" tabIndex={0}>
          {error && (
            <div className="flex items-center gap-2 text-sm text-red-600 dark:text-red-400">
              <AlertCircle className="h-4 w-4 flex-shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {!error && markdown === null && (
            <div className="flex items-center gap-2 text-sm text-gray-500 dark:text-graphite-muted">
              <Loader2 className="h-4 w-4 animate-spin" />
              <span>Načítám…</span>
            </div>
          )}

          {!error && markdown !== null && (
            <div className="prose prose-sm max-w-none dark:prose-invert prose-table:text-xs">
              <ReactMarkdown remarkPlugins={[remarkGfm]}>{markdown}</ReactMarkdown>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default MarginLevelsHelpSheet;
