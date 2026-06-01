import { useCallback } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import ScanInput from '../ScanInput';
import { useLastUsedLotForMaterial } from '../../../api/hooks/useMaterialContainers';

interface LotEntryStepProps {
  mode: 'freeform' | 'po';
}

const LotEntryStep = ({ mode }: LotEntryStepProps) => {
  const navigate = useNavigate();
  const params = useParams<{ material?: string; id?: string; lineId?: string }>();
  const materialCode = params.material ?? '';
  const { data } = useLastUsedLotForMaterial(materialCode);

  const handleScan = useCallback(
    (lot: string) => {
      if (!lot) return;
      if (mode === 'freeform') {
        navigate(
          `/terminal/lot-identification/freeform/${encodeURIComponent(materialCode)}/lot/${encodeURIComponent(lot)}/scan`,
        );
      } else {
        navigate(
          `/terminal/lot-identification/po/${params.id}/line/${params.lineId}/lot/${encodeURIComponent(lot)}/scan`,
        );
      }
    },
    [mode, materialCode, navigate, params.id, params.lineId],
  );

  return (
    <div className="space-y-4 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">
        Šarže pro materiál {materialCode}
      </h2>
      <ScanInput
        label="Šarže (z etikety dodavatele)"
        onScan={handleScan}
        defaultValue={data?.lotCode ?? ''}
      />
    </div>
  );
};

export default LotEntryStep;
