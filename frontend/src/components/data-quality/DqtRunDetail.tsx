import React, { useState } from 'react';
import { Loader2, AlertCircle, ChevronDown, ChevronRight } from 'lucide-react';
import { useDqtRunDetail, InvoiceDqtResultDto } from '../../api/hooks/useDataQuality';

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
  MissingInFlexi: 'bg-red-100 text-red-700',
  MissingInShoptet: 'bg-orange-100 text-orange-700',
  TotalWithVatDiffers: 'bg-yellow-100 text-yellow-700',
  TotalWithoutVatDiffers: 'bg-yellow-100 text-yellow-700',
  ItemsDiffer: 'bg-purple-100 text-purple-700',
};

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
        className="cursor-pointer hover:bg-gray-50 transition-colors"
      >
        <td className="px-3 py-2">
          {expanded ? (
            <ChevronDown className="h-4 w-4 text-gray-400" />
          ) : (
            <ChevronRight className="h-4 w-4 text-gray-400" />
          )}
        </td>
        <td className="px-3 py-2 text-sm font-medium text-gray-900 whitespace-nowrap">
          {result.invoiceCode}
        </td>
        <td className="px-3 py-2">
          <div className="flex flex-wrap gap-1">
            {result.mismatchFlags.map((flag) => (
              <span
                key={flag}
                className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
                  FLAG_COLORS[flag] ?? 'bg-gray-100 text-gray-700'
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
          <td colSpan={3} className="px-3 py-3 bg-gray-50 border-t border-gray-200">
            <div className="grid grid-cols-2 gap-4">
              <div>
                <p className="text-xs font-semibold text-gray-500 mb-1 uppercase tracking-wider">
                  Shoptet
                </p>
                <pre className="text-xs bg-white border border-gray-200 rounded p-2 overflow-auto max-h-48 text-gray-800 whitespace-pre-wrap break-all">
                  {prettyPrint(result.shoptetValue) || '—'}
                </pre>
              </div>
              <div>
                <p className="text-xs font-semibold text-gray-500 mb-1 uppercase tracking-wider">
                  Flexi
                </p>
                <pre className="text-xs bg-white border border-gray-200 rounded p-2 overflow-auto max-h-48 text-gray-800 whitespace-pre-wrap break-all">
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
      <div className="flex items-center justify-center h-40 text-gray-400 text-sm">
        Vyberte test ke zobrazení výsledků.
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-40">
        <div className="flex items-center gap-2 text-gray-500">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          Načítání výsledků...
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-40">
        <div className="flex items-center gap-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          Chyba při načítání výsledků: {(error as Error).message}
        </div>
      </div>
    );
  }

  const results = data?.results ?? [];

  if (results.length === 0) {
    return (
      <div className="flex items-center justify-center h-40 text-gray-400 text-sm">
        Žádné neshody nalezeny pro tento test.
      </div>
    );
  }

  return (
    <div className="overflow-auto">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50 sticky top-0 z-10">
          <tr>
            <th className="px-3 py-3 w-8" />
            <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Faktura
            </th>
            <th className="px-3 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
              Neshody
            </th>
          </tr>
        </thead>
        <tbody className="bg-white divide-y divide-gray-200">
          {results.map((result) => (
            <InvoiceResultRow key={result.id} result={result} />
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default DqtRunDetail;
