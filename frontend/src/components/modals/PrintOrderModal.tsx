import { useState, useEffect } from "react";
import { X, Printer, Hash } from "lucide-react";
import { usePrintExpeditionOrder } from "../../api/hooks/useExpeditionList";
import { getErrorMessage } from "../../utils/errorHandler";

interface PrintOrderModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (orderCode: string) => void;
}

function PrintOrderModal({ isOpen, onClose, onSuccess }: PrintOrderModalProps) {
  const [orderCode, setOrderCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const printOrderMutation = usePrintExpeditionOrder();

  useEffect(() => {
    if (isOpen) {
      setOrderCode("");
      setError(null);
    }
  }, [isOpen]);

  const handleClose = () => {
    if (!printOrderMutation.isPending) {
      onClose();
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (printOrderMutation.isPending) return;

    const trimmed = orderCode.trim();
    if (!trimmed) {
      setError("Zadejte číslo zakázky.");
      return;
    }
    setError(null);

    try {
      const result = await printOrderMutation.mutateAsync({ orderCode: trimmed });
      if (result.success) {
        onSuccess(trimmed);
      } else {
        setError(
          result.errorCode
            ? getErrorMessage(result.errorCode, result.params ?? undefined)
            : "Zakázku se nepodařilo vytisknout.",
        );
      }
    } catch {
      setError("Zakázku se nepodařilo vytisknout. Zkuste to znovu.");
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full">
        <div className="flex items-center justify-between p-6 border-b">
          <div className="flex items-center space-x-2">
            <Printer className="h-5 w-5 text-indigo-600" />
            <h2 className="text-lg font-semibold text-gray-900">Tisknout zakázku</h2>
          </div>
          <button
            onClick={handleClose}
            disabled={printOrderMutation.isPending}
            className="p-2 hover:bg-gray-100 rounded-full transition-colors disabled:opacity-50"
            aria-label="Zavřít"
          >
            <X className="h-5 w-5 text-gray-500" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <p className="text-sm text-gray-600">
            Zadejte číslo zakázky. Zakázka bude vytištěna na expediční list a převedena do stavu „Balí se".
          </p>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <div className="flex items-center space-x-1">
                <Hash className="h-4 w-4" />
                <span>Číslo zakázky</span>
              </div>
            </label>
            <input
              type="text"
              value={orderCode}
              onChange={(e) => setOrderCode(e.target.value)}
              placeholder="např. 0001234"
              autoFocus
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
              disabled={printOrderMutation.isPending}
            />
          </div>

          {error && (
            <div className="p-3 bg-red-100 border border-red-300 text-red-700 rounded-md text-sm">
              {error}
            </div>
          )}

          <div className="flex justify-end space-x-3 pt-2">
            <button
              type="button"
              onClick={handleClose}
              disabled={printOrderMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-md transition-colors disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={printOrderMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md transition-colors disabled:opacity-50 flex items-center space-x-1"
            >
              {printOrderMutation.isPending ? (
                <>
                  <div className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></div>
                  <span>Tisknu...</span>
                </>
              ) : (
                <>
                  <Printer className="h-4 w-4" />
                  <span>Tisknout</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export default PrintOrderModal;
