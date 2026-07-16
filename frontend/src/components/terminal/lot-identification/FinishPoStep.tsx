import { useNavigate, useParams } from 'react-router-dom';
import { CheckCircle } from 'lucide-react';
import { useUpdatePurchaseOrderStatusMutation } from '../../../api/hooks/usePurchaseOrders';
import { UpdatePurchaseOrderStatusRequest } from '../../../api/generated/api-client';
import { useScreenView } from '../../../telemetry/useScreenView';
import { ScanShell } from '../shell/ScanShell';
import LotSessionHeader from './LotSessionHeader';

const FinishPoStep = () => {
  useScreenView('Terminal', 'LotIdentificationFinishPo');
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const update = useUpdatePurchaseOrderStatusMutation();

  const handleConfirm = () => {
    update.mutate(
      { id: poId, request: new UpdatePurchaseOrderStatusRequest({ id: poId, status: 'Received' }) },
      {
        onSuccess: () => navigate('/terminal/lot-identification'),
      },
    );
  };

  const subject = <LotSessionHeader title="Dokončit příjem objednávky" icon={CheckCircle} facts="Potvrďte stav objednávky" />;

  return (
    <ScanShell
      subject={subject}
      actions={[
        {
          label: 'Ponechat ve stavu „V přepravě"',
          onClick: () => navigate('/terminal/lot-identification'),
          variant: 'ghost',
        },
        {
          label: 'Označit jako přijatou',
          onClick: handleConfirm,
          variant: 'success',
          loading: update.isPending,
        },
      ]}
    >
      <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">
        Označit objednávku jako přijatou?
      </h2>
      <p className="mt-2 text-sm text-neutral-gray dark:text-graphite-muted">
        Všechny přijaté kontejnery jsou uložené. Můžete objednávku označit jako přijatou, nebo ji ponechat
        ve stavu „V přepravě" a dokončit později.
      </p>
    </ScanShell>
  );
};

export default FinishPoStep;
