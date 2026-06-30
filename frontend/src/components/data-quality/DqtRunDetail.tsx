import React, { useState } from 'react';
import { Loader2, AlertCircle, ChevronDown, ChevronRight } from 'lucide-react';
import { useDqtRunDetail, InvoiceDqtResultDto, DqtDriftResultDto } from '../../api/hooks/useDataQuality';

interface DqtRunDetailProps {
  runId: string | null;
}

const MISMATCH_FLAG_LABELS: Record<string, string> = {
  MissingInFlexi: 'Chybí ve Flexi',
  MissingInShoptet: 'Chybí v Shoptetu',
  TotalWithVatDiffers: 'Celkem s DPH',
  TotalWithoutVatDiffers: 'Celkem bez DPH',
  ItemsDiffer: 'Položky',
};

const FLAG_COLORS: Record<string, string> = {
  MissingInFlexi: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
  MissingInShoptet: 'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300',
  TotalWithVatDiffers: 'bg-yellow-100 text-yellow-700 dark:bg-amber-900/30 dark:text-amber-300',
  TotalWithoutVatDiffers: 'bg-yellow-100 text-yellow-700 dark:bg-amber-900/30 dark:text-amber-300',
  ItemsDiffer: 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300',
};

const PRODUCT_PAIRING_FLAGS: Record<number, string> = {
  1: 'Chybí v ERP',
  2: 'Chybí v Shoptet',
  4: 'Nespárovaný párový kód',
};

const STOCK_WRITE_BACK_FLAGS: Record<number, string> = {
  1: 'Operace selhala',
  2: 'Operace zaseknutá',
  4: 'Chyba inventury',
};

function decodeMismatchFlags(code: number, labels: Record<number, string>): string[] {
  return Object.entries(labels)
    .filter(([flag]) => (code & Number(flag)) !== 0)
    .map(([, label]) => label);
}

const prettyPrint = (raw: string | null): string => {
  if (raw == null) return '';
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
};

const InvoiceResultRow: React.FC<{ result: InvoiceDqtResultDto }> = ({ result }) => {
  const [expanded, setExpanded] = useState(false);

  return (
    <>
      <tr
        onClick={() => setExpanded((v) => !v)}
        className="cursor-pointer hover:bg-gray-50 dark:hover:bg-white/5 transition-colors"
      >
        <td className="px-3 py-2">
          {expanded ? (
            <ChevronDown className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
          ) : (
            <ChevronRight className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
          )}
        </td>
        <td className="px-3 py-2 text-sm font-medium text-gray-900 dark:text-graphite-text whitespace-nowrap">
          {result.invoiceCode}
        </td>
        <td className="px-3 py-2">
          <div className="flex flex-wrap gap-1">
            {result.mismatchFlags.map((flag) => (
              <span
                key={flag}
                className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
                  FLAG_COLORS[flag] ?? 'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted'
                }`}
              >
                {MISMATCH_FLAG_LABELS[flag] ?? flag}
              </span>
            ))}
          </div>
        </td>
      </tr>
      {expanded && (
        <tr>
          <td colSpan={3} className="px-3 py-3 bg-gray-50 dark:bg-graphite-surface-2 border-t border-gray-200 dark:border-graphite-border">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <p className="text-xs font-semibold text-gray-500 dark:text-graphite-muted mb-1 uppercase tracking-wider">
                  Shoptet
                </p>
                <pre className="text-xs bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded p-2 overflow-auto max-h-48 text-gray-800 dark:text-graphite-muted whitespace-pre-wrap break-all">
                  {prettyPrint(result.shoptetValue) || '—'}
                </pre>
              </div>
              <div>
                <p className="text-xs font-semibold text-gray-500 dark:text-graphite-muted mb-1 uppercase tracking-wider">
                  Flexi
                </p>
                <pre className="text-xs bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border rounded p-2 overflow-auto max-h-48 text-gray-800 dark:text-graphite-muted whitespace-pre-wrap break-all">
                  {prettyPrint(result.flexiValue) || '—'}
                </pre>
              </div>
            </div>
          </td>
        </tr>
      )}
    </>
  );
};

const DqtRunDetail: React.FC<DqtRunDetailProps> = ({ runId }) => {
  const { data, isLoading, error } = useDqtRunDetail(runId);

  if (!runId) {
    return (
      <div className="flex items-center justify-center h-40 text-gray-400 dark:text-graphite-faint text-sm">
        Vyberte test ke zobrazení výsledků.
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-40">
        <div className="flex items-center gap-2 text-gray-500 dark:text-graphite-muted">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500 dark:text-graphite-accent" />
          Načítání výsledků...
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-40">
        <div className="flex items-center gap-2 text-red-600 dark:text-red-400">
          <AlertCircle className="h-5 w-5" />
          Chyba při načítání výsledků: {(error as Error).message}
        </div>
      </div>
    );
  }

  const run = data?.run ?? null;
  const results = data?.results ?? [];
  const driftResults = data?.driftResults ?? [];

  const isDriftTestType =
    run?.testType === 'ProductPairing' || run?.testType === 'StockWriteBackReconciliation';

  const hasNoResults = isDriftTestType ? driftResults.length === 0 : results.length === 0;

  if (hasNoResults) {
    return (
      <div className="flex items-center justify-center h-40 text-gray-400 dark:text-graphite-faint text-sm">
        Žádné neshody nalezeny pro tento test.
      </div>
    );
  }

  if (isDriftTestType) {
    const flagMap =
      run?.testType === 'ProductPairing' ? PRODUCT_PAIRING_FLAGS : STOCK_WRITE_BACK_FLAGS;

    return (
      <div className="overflow-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b text-left">
              <th className="py-2 pr-4 font-medium">Entita</th>
              <th className="py-2 pr-4 font-medium">Neshoda</th>
              <th className="py-2 pr-4 font-medium">Heblo</th>
              <th className="py-2 pr-4 font-medium">Shoptet</th>
              <th className="py-2 font-medium">Detail</th>
            </tr>
          </thead>
          <tbody>
            {driftResults.map((row: DqtDriftResultDto, i: number) => {
              const flagLabels = decodeMismatchFlags(row.mismatchCode, flagMap);
              return (
                <tr key={i} className="border-b last:border-0">
                  <td className="py-1.5 pr-4 font-mono text-xs">{row.entityKey}</td>
                  <td className="py-1.5 pr-4">
                    {flagLabels.map((label) => (
                      <span
                        key={label}
                        className="inline-block mr-1 px-1.5 py-0.5 rounded text-xs bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300"
                      >
                        {label}
                      </span>
                    ))}
                  </td>
                  <td className="py-1.5 pr-4 text-gray-600 dark:text-graphite-muted text-xs">{row.hebloValue ?? '—'}</td>
                  <td className="py-1.5 pr-4 text-gray-600 dark:text-graphite-muted text-xs">{row.shoptetValue ?? '—'}</td>
                  <td className="py-1.5 text-gray-500 dark:text-graphite-muted text-xs">{row.details ?? ''}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    );
  }

  return (
    <div className="overflow-auto">
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
          <tr>
            <th className="px-3 py-3 w-8" />
            <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
              Faktura
            </th>
            <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
              Neshody
            </th>
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {results.map((result) => (
            <InvoiceResultRow key={result.id} result={result} />
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default DqtRunDetail;
