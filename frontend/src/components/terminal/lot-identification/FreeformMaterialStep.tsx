import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import ScanInput from '../ScanInput';

const FreeformMaterialStep = () => {
  const navigate = useNavigate();

  const handleScan = useCallback(
    (value: string) => {
      if (!value) return;
      navigate(`/terminal/lot-identification/freeform/${encodeURIComponent(value)}/lot`);
    },
    [navigate],
  );

  return (
    <div className="space-y-4 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">Kód materiálu</h2>
      <ScanInput label="Naskenujte nebo zadejte kód materiálu" onScan={handleScan} />
    </div>
  );
};

export default FreeformMaterialStep;
