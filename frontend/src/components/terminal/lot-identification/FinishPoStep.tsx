import { useNavigate, useParams } from 'react-router-dom';
import { useUpdatePurchaseOrderStatusMutation } from '../../../api/hooks/usePurchaseOrders';
import { UpdatePurchaseOrderStatusRequest } from '../../../api/generated/api-client';

const FinishPoStep = () => {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const update = useUpdatePurchaseOrderStatusMutation();

  const handleConfirm = () => {
    update.mutate(
      { id: poId, request: new UpdatePurchaseOrderStatusRequest({ id: poId, status: 'Received' }) },
      {
        onSuccess: () => navigate('/terminal/lot-identification'),
      }
    );
  };

  return (
    <div className="space-y-4 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">
        Označit objednávku jako přijatou?
      </h2>
      <button
        type="button"
        disabled={update.isPending}
        onClick={handleConfirm}
        className="w-full h-12 bg-primary-blue text-white rounded-xl font-semibold disabled:opacity-50"
      >
        Označit jako přijatou
      </button>
      <button
        type="button"
        onClick={() => navigate('/terminal/lot-identification')}
        className="w-full h-12 border border-border-light dark:border-graphite-border text-neutral-slate dark:text-graphite-text rounded-xl"
      >
        Ponechat ve stavu „V přepravě"
      </button>
    </div>
  );
};

export default FinishPoStep;
