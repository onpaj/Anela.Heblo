import { Link } from 'react-router-dom';
import { ChevronRight, ClipboardList } from 'lucide-react';
import { usePurchaseOrdersQuery } from '../../../api/hooks/usePurchaseOrders';

const PoPickStep = () => {
  const { data, isLoading } = usePurchaseOrdersQuery({ status: 'InTransit' });

  if (isLoading) {
    return <p className="text-sm text-neutral-gray dark:text-graphite-muted">Načítám objednávky…</p>;
  }

  if (!data?.orders?.length) {
    return <p className="text-sm text-neutral-gray dark:text-graphite-muted">Žádné objednávky v přepravě.</p>;
  }

  return (
    <div className="space-y-3 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate dark:text-graphite-text">Vyberte objednávku</h2>
      {data.orders.map((po) => (
        <Link
          key={po.id}
          to={`/terminal/lot-identification/po/${po.id}`}
          className="flex items-center gap-3 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-3 hover:border-primary-blue dark:hover:border-graphite-accent"
        >
          <ClipboardList className="h-5 w-5 text-primary-blue dark:text-graphite-accent flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-neutral-slate dark:text-graphite-text">{po.orderNumber}</p>
            <p className="text-sm text-neutral-gray dark:text-graphite-muted">{po.supplierName}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray dark:text-graphite-muted" />
        </Link>
      ))}
    </div>
  );
};

export default PoPickStep;
