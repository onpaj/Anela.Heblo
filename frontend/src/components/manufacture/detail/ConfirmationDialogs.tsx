import React from "react";
import {
  AlertCircle,
  Loader2,
  X,
  StickyNote,
} from "lucide-react";
import { ManufactureOrderState, ManufactureType, ResidueDistributionDto } from "../../../api/generated/api-client";
import ConfirmSemiProductQuantityModal from "../../modals/ConfirmSemiProductQuantityModal";
import ConfirmProductCompletionModal from "../../modals/ConfirmProductCompletionModal";
import ResolveManualActionModal from "../../modals/ResolveManualActionModal";

interface ConfirmationDialogsProps {
  // Cancel confirmation
  showCancelConfirmation: boolean;
  onCancelConfirmationClose: () => void;
  onCancelConfirm: () => void;
  
  // State return confirmation
  showStateReturnConfirmation: boolean;
  pendingStateChange: ManufactureOrderState | null;
  onStateReturnConfirmationClose: () => void;
  onStateReturnConfirm: () => void;
  
  // Quantity confirmation modal
  showQuantityConfirmModal: boolean;
  onQuantityConfirmModalClose: () => void;
  onQuantityConfirm: (request: any) => Promise<void>;
  
  // Product completion modal
  showProductCompletionModal: boolean;
  onProductCompletionModalClose: () => void;
  onProductCompletionConfirm: (request: any) => Promise<void>;
  distributionPreview?: ResidueDistributionDto;
  onConfirmDistribution: () => Promise<void>;
  onBackFromDistribution: () => void;
  
  // Resolve manual action modal
  showResolveModal: boolean;
  onResolveModalClose: () => void;
  onResolveSuccess: () => void;
  
  // Expanded note modal
  showExpandedNote: boolean;
  expandedNoteContent: string;
  onExpandedNoteClose: () => void;
  
  // Common props
  order: any;
  orderId: number;
  isUpdateLoading: boolean;
  isQuantityLoading: boolean;
  isProductCompletionLoading: boolean;
  getStateLabel: (state: ManufactureOrderState) => string;
}

export const ConfirmationDialogs: React.FC<ConfirmationDialogsProps> = ({
  showCancelConfirmation,
  onCancelConfirmationClose,
  onCancelConfirm,
  showStateReturnConfirmation,
  pendingStateChange,
  onStateReturnConfirmationClose,
  onStateReturnConfirm,
  showQuantityConfirmModal,
  onQuantityConfirmModalClose,
  onQuantityConfirm,
  showProductCompletionModal,
  onProductCompletionModalClose,
  onProductCompletionConfirm,
  distributionPreview,
  onConfirmDistribution,
  onBackFromDistribution,
  showResolveModal,
  onResolveModalClose,
  onResolveSuccess,
  showExpandedNote,
  expandedNoteContent,
  onExpandedNoteClose,
  order,
  orderId,
  isUpdateLoading,
  isQuantityLoading,
  isProductCompletionLoading,
  getStateLabel,
}) => {
  return (
    <>
      {/* Cancel Confirmation Dialog */}
      {showCancelConfirmation && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-[60]">
          <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full">
            <div className="p-6">
              <div className="flex items-center mb-4">
                <AlertCircle className="h-6 w-6 text-red-600 mr-3" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
                  Stornovat zakázku
                </h3>
              </div>
              <p className="text-gray-600 dark:text-graphite-muted mb-6">
                Opravdu chcete stornovat tuto výrobní zakázku? Tato akce je nevratná.
              </p>
              <div className="flex justify-end space-x-3">
                <button
                  onClick={onCancelConfirmationClose}
                  className="px-4 py-2 text-gray-700 dark:text-graphite-muted bg-gray-100 dark:bg-graphite-surface-2 rounded-lg hover:bg-gray-200 dark:hover:bg-graphite-hover transition-colors"
                >
                  Zrušit
                </button>
                <button
                  onClick={onCancelConfirm}
                  disabled={isUpdateLoading}
                  className="px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {isUpdateLoading ? (
                    <>
                      <Loader2 className="h-4 w-4 mr-1 animate-spin inline" />
                      Stornuji...
                    </>
                  ) : (
                    'Stornovat zakázku'
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* State Return Confirmation Dialog */}
      {showStateReturnConfirmation && pendingStateChange && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-[60]">
          <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full">
            <div className="p-6">
              <div className="flex items-center mb-4">
                <AlertCircle className="h-6 w-6 text-amber-600 mr-3" />
                <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">
                  Potvrdit návrat stavu
                </h3>
              </div>
              <p className="text-gray-600 dark:text-graphite-muted mb-6">
                Opravdu chcete vrátit zakázku zpět ze stavu "{order && order.state !== undefined ? getStateLabel(order.state) : 'Neznámý'}" na "{getStateLabel(pendingStateChange)}"?
              </p>
              <div className="flex justify-end space-x-3">
                <button
                  onClick={onStateReturnConfirmationClose}
                  className="px-4 py-2 text-gray-700 dark:text-graphite-muted bg-gray-100 dark:bg-graphite-surface-2 rounded-lg hover:bg-gray-200 dark:hover:bg-graphite-hover transition-colors"
                >
                  Zrušit
                </button>
                <button
                  onClick={onStateReturnConfirm}
                  disabled={isUpdateLoading}
                  className="px-4 py-2 bg-amber-600 text-white rounded-lg hover:bg-amber-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {isUpdateLoading ? (
                    <>
                      <Loader2 className="h-4 w-4 mr-1 animate-spin inline" />
                      Vracím...
                    </>
                  ) : (
                    'Potvrdit návrat'
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Confirm Semi-Product Quantity Modal */}
      {showQuantityConfirmModal && order?.semiProduct && (
        <ConfirmSemiProductQuantityModal
          isOpen={showQuantityConfirmModal}
          onClose={onQuantityConfirmModalClose}
          onSubmit={onQuantityConfirm}
          orderId={orderId}
          plannedQuantity={order.semiProduct.plannedQuantity || 0}
          productName={order.semiProduct.productName || ""}
          isLoading={isQuantityLoading}
        />
      )}

      {/* Confirm Product Completion Modal */}
      {showProductCompletionModal && order?.products && order.products.length > 0 && (
        <ConfirmProductCompletionModal
          isOpen={showProductCompletionModal}
          onClose={onProductCompletionModalClose}
          onSubmit={onProductCompletionConfirm}
          orderId={orderId}
          products={order.products.map((product: any) => ({
            id: product.id || 0,
            productCode: product.productCode || "",
            productName: product.productName || "",
            plannedQuantity: product.plannedQuantity || 0
          }))}
          semiProductCode={order.manufactureType !== ManufactureType.SinglePhase ? order.semiProduct?.productCode : undefined}
          isLoading={isProductCompletionLoading}
          distributionPreview={distributionPreview}
          onConfirmDistribution={onConfirmDistribution}
          onBackFromDistribution={onBackFromDistribution}
        />
      )}

      {/* Resolve Manual Action Modal */}
      {showResolveModal && order && (
        <ResolveManualActionModal
          isOpen={showResolveModal}
          onClose={onResolveModalClose}
          orderId={orderId!}
          currentErpSemiproduct={order.erpOrderNumberSemiproduct || ""}
          currentErpProduct={order.erpOrderNumberProduct || ""}
          currentErpDiscardResidueDocumentNumber={order.erpDiscardResidueDocumentNumber || ""}
          onSuccess={onResolveSuccess}
        />
      )}

      {/* Expanded Note Modal */}
      {showExpandedNote && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-[70]">
          <div className="bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-2xl w-full max-h-[80vh] overflow-hidden">
            <div className="p-6">
              <div className="flex items-center justify-between mb-4">
                <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text flex items-center">
                  <StickyNote className="h-5 w-5 text-yellow-600 dark:text-amber-400 mr-2" />
                  Poznámka
                </h3>
                <button
                  onClick={onExpandedNoteClose}
                  className="text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted transition-colors"
                >
                  <X className="h-6 w-6" />
                </button>
              </div>
              <div className="bg-yellow-50 border border-yellow-200 dark:bg-amber-900/20 dark:border-amber-900/40 rounded-lg p-4 max-h-[60vh] overflow-y-auto">
                <p className="text-gray-800 dark:text-graphite-muted whitespace-pre-wrap text-sm leading-relaxed">
                  {expandedNoteContent}
                </p>
              </div>
              <div className="mt-4 flex justify-end">
                <button
                  onClick={onExpandedNoteClose}
                  className="px-4 py-2 bg-gray-100 dark:bg-graphite-surface-2 text-gray-700 dark:text-graphite-muted rounded-lg hover:bg-gray-200 dark:hover:bg-graphite-hover transition-colors"
                >
                  Zavřít
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default ConfirmationDialogs;