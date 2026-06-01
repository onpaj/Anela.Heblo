import { useState, useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { CheckCircle } from 'lucide-react';
import ScanInput from '../ScanInput';
import { useCreateMaterialContainers } from '../../../api/hooks/useMaterialContainers';
import { CreateMaterialContainerItem } from '../../../api/generated/api-client';
import { ErrorCodes } from '../../../types/errors';

const CODE_FORMAT = /^M\d{8}$/;
const INVALID_FORMAT_MESSAGE = 'Neplatný formát kódu (očekáváno M + 8 číslic).';
const CONNECTION_ERROR_MESSAGE = 'Chyba připojení.';
const GENERIC_SAVE_ERROR_MESSAGE = 'Chyba při ukládání kontejneru.';

interface ContainerScanLoopProps {
  mode: 'freeform' | 'po';
}

const ContainerScanLoop = ({ mode }: ContainerScanLoopProps) => {
  const params = useParams<{ material: string; lot: string; id?: string; lineId?: string }>();
  const materialCode = params.material ?? '';
  const lotCode = params.lot ?? '';
  const poLineId = params.lineId ? parseInt(params.lineId, 10) : undefined;
  const navigate = useNavigate();

  const [count, setCount] = useState(0);
  const [lastSaved, setLastSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const create = useCreateMaterialContainers();

  const handleScan = useCallback(
    (raw: string) => {
      const code = raw.trim();
      if (!CODE_FORMAT.test(code)) {
        setError(INVALID_FORMAT_MESSAGE);
        setLastSaved(false);
        return;
      }
      setError(null);
      setLastSaved(false);
      create.mutate(
        { items: [new CreateMaterialContainerItem({ code, materialCode, lotCode, purchaseOrderLineId: poLineId })] },
        {
          onSuccess: (data) => {
            if (data.success) {
              setCount((c) => c + 1);
              setLastSaved(true);
            } else if (data.errorCode === ErrorCodes.MaterialContainerCodeExists) {
              const status = data.params?.['Status'];
              const assignedMaterial = data.params?.['MaterialCode'];
              const assignedLot = data.params?.['LotCode'];
              const message =
                status === 'Discarded'
                  ? 'Tento kód byl vyřazen, použijte nový štítek.'
                  : `Kód ${code} je již přiřazen k materiálu ${assignedMaterial} / šarži ${assignedLot}.`;
              setError(message);
            } else {
              setError(GENERIC_SAVE_ERROR_MESSAGE);
            }
          },
          onError: () => setError(CONNECTION_ERROR_MESSAGE),
        },
      );
    },
    [materialCode, lotCode, poLineId, create],
  );

  const handleFinish = () => {
    if (mode === 'po') {
      navigate(`/terminal/lot-identification/po/${params.id}/finish`);
    } else {
      navigate('/terminal/lot-identification');
    }
  };

  return (
    <div className="space-y-4 pt-2">
      <div className="bg-secondary-blue-pale border border-primary-blue rounded-xl p-3 space-y-1">
        <p className="text-xs text-neutral-gray">Materiál</p>
        <p className="font-mono font-semibold text-neutral-slate">{materialCode}</p>
        <p className="text-xs text-neutral-gray mt-1">Šarže</p>
        <p className="font-mono font-semibold text-neutral-slate">{lotCode}</p>
        <p className="text-xs text-neutral-gray mt-2">
          Naskenováno: <span className="font-semibold">{count}</span>
        </p>
      </div>

      <ScanInput
        label="Kód kontejneru (Mxxxxxxxx)"
        onScan={handleScan}
        loading={create.isPending}
      />

      {error && (
        <p role="alert" className="text-sm text-red-600">
          {error}
        </p>
      )}

      {lastSaved && !error && !create.isPending && (
        <p className="text-sm text-green-600 flex items-center gap-1">
          <CheckCircle className="h-4 w-4" /> Uloženo
        </p>
      )}

      <button
        type="button"
        onClick={handleFinish}
        className="w-full h-12 bg-primary-blue text-white rounded-xl font-semibold"
      >
        Hotovo
      </button>
    </div>
  );
};

export default ContainerScanLoop;
