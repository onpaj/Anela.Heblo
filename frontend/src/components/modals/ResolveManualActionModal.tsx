import React, { useState, useEffect } from "react";
import { X, AlertCircle, Hash, MessageSquare, Check } from "lucide-react";
import { useResolveManualAction } from "../../api/hooks/useManufactureOrders";
import { ResolveManualActionRequest } from "../../api/generated/api-client";

interface ResolveManualActionModalProps {
  isOpen: boolean;
  onClose: () => void;
  orderId: number;
  currentErpSemiproduct: string;
  currentErpProduct: string;
  currentErpDiscardResidueDocumentNumber: string;
  onSuccess: () => void;
}

const ResolveManualActionModal: React.FC<ResolveManualActionModalProps> = ({
  isOpen,
  onClose,
  orderId,
  currentErpSemiproduct,
  currentErpProduct,
  currentErpDiscardResidueDocumentNumber,
  onSuccess,
}) => {
  const [erpSemiproduct, setErpSemiproduct] = useState(currentErpSemiproduct);
  const [erpProduct, setErpProduct] = useState(currentErpProduct);
  const [erpDiscardResidueDocumentNumber, setErpDiscardResidueDocumentNumber] = useState(currentErpDiscardResidueDocumentNumber);
  const [note, setNote] = useState("");
  
  const resolveManualActionMutation = useResolveManualAction();

  // Reset form when modal opens
  useEffect(() => {
    if (isOpen) {
      setErpSemiproduct(currentErpSemiproduct);
      setErpProduct(currentErpProduct);
      setErpDiscardResidueDocumentNumber(currentErpDiscardResidueDocumentNumber);
      setNote("");
    }
  }, [isOpen, currentErpSemiproduct, currentErpProduct, currentErpDiscardResidueDocumentNumber]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    if (resolveManualActionMutation.isPending) return;

    try {
      const request = new ResolveManualActionRequest({
        orderId,
        erpOrderNumberSemiproduct: erpSemiproduct || undefined,
        erpOrderNumberProduct: erpProduct || undefined,
        erpDiscardResidueDocumentNumber: erpDiscardResidueDocumentNumber || undefined,
        note: note.trim() || undefined
      });

      await resolveManualActionMutation.mutateAsync(request);
      onSuccess();
    } catch (error) {
      console.error("Chyba při řešení ručního zásahu:", error);
      alert("Nastala chyba při řešení ručního zásahu. Zkuste to znovu.");
    }
  };

  const handleClose = () => {
    if (!resolveManualActionMutation.isPending) {
      onClose();
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
      <div className="bg-white rounded-lg shadow-xl max-w-md w-full max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b">
          <div className="flex items-center space-x-2">
            <AlertCircle className="h-5 w-5 text-orange-600" />
            <h2 className="text-lg font-semibold text-gray-900">
              Vyřešit ruční zásah
            </h2>
          </div>
          <button
            onClick={handleClose}
            disabled={resolveManualActionMutation.isPending}
            className="p-2 hover:bg-gray-100 rounded-full transition-colors disabled:opacity-50"
          >
            <X className="h-5 w-5 text-gray-500" />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <p className="text-sm text-gray-600">
            Označte problém za vyřešený zadáním ERP čísel zakázky a volitelnou poznámkou.
          </p>

          {/* ERP Semiproduct */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <div className="flex items-center space-x-1">
                <Hash className="h-4 w-4" />
                <span>ERP číslo meziproduktu</span>
              </div>
            </label>
            <input
              type="text"
              value={erpSemiproduct}
              onChange={(e) => setErpSemiproduct(e.target.value)}
              placeholder="Zadejte ERP číslo pro meziprodukt"
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
              disabled={resolveManualActionMutation.isPending}
            />
          </div>

          {/* ERP Product */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <div className="flex items-center space-x-1">
                <Hash className="h-4 w-4" />
                <span>ERP číslo produktu</span>
              </div>
            </label>
            <input
              type="text"
              value={erpProduct}
              onChange={(e) => setErpProduct(e.target.value)}
              placeholder="Zadejte ERP číslo pro produkt"
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
              disabled={resolveManualActionMutation.isPending}
            />
          </div>

          {/* ERP Discard Residue Document Number */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <div className="flex items-center space-x-1">
                <Hash className="h-4 w-4" />
                <span>ERP číslo dokladu likvidace zbytku</span>
              </div>
            </label>
            <input
              type="text"
              value={erpDiscardResidueDocumentNumber}
              onChange={(e) => setErpDiscardResidueDocumentNumber(e.target.value)}
              placeholder="Zadejte ERP číslo dokladu likvidace zbytku"
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500"
              disabled={resolveManualActionMutation.isPending}
            />
          </div>

          {/* Note */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <div className="flex items-center space-x-1">
                <MessageSquare className="h-4 w-4" />
                <span>Poznámka (volitelná)</span>
              </div>
            </label>
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder="Popište jak byl problém vyřešen..."
              rows={3}
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:ring-indigo-500 focus:border-indigo-500 resize-none"
              disabled={resolveManualActionMutation.isPending}
            />
          </div>

          {/* Buttons */}
          <div className="flex justify-end space-x-3 pt-4">
            <button
              type="button"
              onClick={handleClose}
              disabled={resolveManualActionMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-md transition-colors disabled:opacity-50"
            >
              Zrušit
            </button>
            <button
              type="submit"
              disabled={resolveManualActionMutation.isPending}
              className="px-4 py-2 text-sm font-medium text-white bg-green-600 hover:bg-green-700 rounded-md transition-colors disabled:opacity-50 flex items-center space-x-1"
            >
              {resolveManualActionMutation.isPending ? (
                <>
                  <div className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full"></div>
                  <span>Ukládám...</span>
                </>
              ) : (
                <>
                  <Check className="h-4 w-4" />
                  <span>Označit za vyřešené</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

export default ResolveManualActionModal;