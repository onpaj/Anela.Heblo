import { Link, useParams } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';
import { usePurchaseOrderDetailQuery } from '../../../api/hooks/usePurchaseOrders';

const PoLinePickStep = () => {
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const { data, isLoading } = usePurchaseOrderDetailQuery(poId);

  if (isLoading || !data) {
    return <p className="text-sm text-neutral-gray dark:text-graphite-muted">Načítám…</p>;
  }

  return (
    <div className="space-y-3 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">
        {data.orderNumber} — položky
      </h2>
      {(data.lines ?? []).map((line) => (
        <Link
          key={line.id}
          to={`/terminal/lot-identification/po/${poId}/line/${line.id}/material/${encodeURIComponent(line.materialId ?? '')}/lot`}
          className="flex items-center gap-3 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-3 hover:border-primary-blue dark:hover:border-graphite-accent"
        >
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-neutral-slate dark:text-graphite-text">{line.materialName}</p>
            <p className="text-sm text-neutral-gray dark:text-graphite-muted">{line.materialId}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray dark:text-graphite-muted" />
        </Link>
      ))}
    </div>
  );
};

export default PoLinePickStep;
