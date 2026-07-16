import { Link, useNavigate, useParams } from 'react-router-dom';
import { ChevronRight, FlaskConical } from 'lucide-react';
import { usePurchaseOrderDetailQuery } from '../../../api/hooks/usePurchaseOrders';
import { useScreenView } from '../../../telemetry/useScreenView';
import { ScanShell } from '../shell/ScanShell';
import LotSessionHeader from './LotSessionHeader';

const PoLinePickStep = () => {
  useScreenView('Terminal', 'LotIdentificationPoLinePick');
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const { data, isLoading } = usePurchaseOrderDetailQuery(poId);

  if (isLoading || !data) {
    return (
      <ScanShell subject={<LotSessionHeader title="Příjem podle objednávky" facts="Načítám…" />}>
        <p className="text-sm text-neutral-gray dark:text-graphite-muted">Načítám…</p>
      </ScanShell>
    );
  }

  const lines = data.lines ?? [];
  const subject = <LotSessionHeader title={data.orderNumber ?? ''} facts={`${lines.length} položek`} />;

  return (
    <ScanShell
      subject={subject}
      actions={[
        {
          label: 'Dokončit příjem objednávky',
          onClick: () => navigate(`/terminal/lot-identification/po/${poId}/finish`),
          variant: 'primary',
        },
      ]}
    >
      <p className="mb-3 text-sm text-neutral-gray dark:text-graphite-muted">
        Vyberte materiál a naskenujte přijaté kontejnery. Až budete hotovi, dokončete objednávku.
      </p>
      <div className="space-y-2">
        {lines.map((line) => (
          <Link
            key={line.id}
            to={`/terminal/lot-identification/po/${poId}/line/${line.id}/material/${encodeURIComponent(line.materialId ?? '')}`}
            state={{ materialName: line.materialName }}
            className="flex items-center gap-3 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-3 hover:border-primary-blue dark:hover:border-graphite-accent transition-colors"
          >
            <span className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-violet-50 dark:bg-graphite-surface-2 text-violet-600 dark:text-violet-400">
              <FlaskConical className="h-5 w-5" />
            </span>
            <div className="flex-1 min-w-0">
              <p className="font-semibold text-neutral-slate dark:text-graphite-text truncate">{line.materialName}</p>
              <p className="font-mono text-sm text-neutral-gray dark:text-graphite-muted truncate">{line.materialId}</p>
            </div>
            <ChevronRight className="h-5 w-5 flex-shrink-0 text-neutral-gray dark:text-graphite-muted" />
          </Link>
        ))}
      </div>
    </ScanShell>
  );
};

export default PoLinePickStep;
