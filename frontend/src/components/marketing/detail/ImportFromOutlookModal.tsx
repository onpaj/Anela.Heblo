import React, { useState, useCallback } from 'react';
import { Download } from 'lucide-react';
import {
  useImportFromOutlook,
  type ImportFromOutlookResult,
} from '../../../api/hooks/useMarketingCalendar';

interface ImportFromOutlookModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const ImportFromOutlookModal: React.FC<ImportFromOutlookModalProps> = ({ isOpen, onClose }) => {
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [dryRun, setDryRun] = useState(false);
  const [result, setResult] = useState<ImportFromOutlookResult | null>(null);

  const importMutation = useImportFromOutlook();

  const handleOpen = useCallback(() => {
    setFromDate('');
    setToDate('');
    setDryRun(false);
    setResult(null);
    importMutation.reset();
  }, [importMutation]);

  // Reset state when modal opens
  React.useEffect(() => {
    if (isOpen) handleOpen();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen]);

  const handleImport = useCallback(async () => {
    if (!fromDate || !toDate) return;
    setResult(null);
    try {
      const response = await importMutation.mutateAsync({
        fromUtc: new Date(fromDate),
        // End of day in UTC; fromUtc is local midnight (consistent with calendar date pickers elsewhere)
        toUtc: new Date(toDate + 'T23:59:59Z'),
        dryRun,
      });
      const data = response as ImportFromOutlookResult;
      setResult({
        created: data?.created ?? 0,
        skipped: data?.skipped ?? 0,
        failed: data?.failed ?? 0,
      });
    } catch {
      // importMutation.isError is set by react-query
    }
  }, [fromDate, toDate, dryRun, importMutation]);

  if (!isOpen) return null;

  const modalTitleId = 'import-from-outlook-title';

  return (
    <div
      className='fixed inset-0 z-50 flex items-center justify-center bg-black/50'
      role='dialog'
      aria-modal='true'
      aria-labelledby={modalTitleId}
    >
      <div className='bg-white rounded-xl shadow-xl w-full max-w-md p-6'>
        <h2 id={modalTitleId} className='text-lg font-semibold text-gray-900 mb-4'>
          Import z Outlooku
        </h2>
        <div className='space-y-4'>
          <div>
            <label htmlFor='import-from-date' className='block text-sm font-medium text-gray-700 mb-1'>Od</label>
            <input
              id='import-from-date'
              type='date'
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
              className='w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500'
            />
          </div>
          <div>
            <label htmlFor='import-to-date' className='block text-sm font-medium text-gray-700 mb-1'>Do</label>
            <input
              id='import-to-date'
              type='date'
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
              className='w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500'
            />
          </div>
          <div className='flex items-center gap-2'>
            <input
              id='dryRun'
              type='checkbox'
              checked={dryRun}
              onChange={(e) => setDryRun(e.target.checked)}
              className='h-4 w-4 text-indigo-600 border-gray-300 rounded'
            />
            <label htmlFor='dryRun' className='text-sm text-gray-700'>Jen simulace (dry run)</label>
          </div>

          {result && (
            <div className='rounded-lg bg-gray-50 border border-gray-200 p-3 text-sm text-gray-700'>
              <p>Vytvořeno: <strong>{result.created}</strong></p>
              <p>Přeskočeno: <strong>{result.skipped}</strong></p>
              <p>Chyb: <strong>{result.failed}</strong></p>
            </div>
          )}

          {importMutation.isError && (
            <p className='text-sm text-red-600'>Import selhal. Zkuste to znovu.</p>
          )}
        </div>

        <div className='mt-6 flex justify-end gap-3'>
          <button
            onClick={onClose}
            className='px-4 py-2 text-sm text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors'
          >
            Zavřít
          </button>
          <button
            disabled={!fromDate || !toDate || importMutation.isPending}
            onClick={handleImport}
            className='px-4 py-2 text-sm font-medium text-white bg-indigo-600 rounded-lg hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors'
          >
            {importMutation.isPending ? (
              'Importuji...'
            ) : (
              <>
                <Download className='inline h-4 w-4 mr-1' />
                Importovat
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};

export default ImportFromOutlookModal;
