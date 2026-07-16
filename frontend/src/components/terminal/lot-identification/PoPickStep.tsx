import { Link } from 'react-router-dom';
import { ChevronRight, ClipboardList } from 'lucide-react';
import { usePurchaseOrdersQuery } from '../../../api/hooks/usePurchaseOrders';
import { useScreenView } from '../../../telemetry/useScreenView';
import { ScanShell } from '../shell/ScanShell';
import LotSessionHeader from './LotSessionHeader';

const PoPickStep = () => {
  useScreenView('Terminal', 'LotIdentificationPoPick');
  const { data, isLoading } = usePurchaseOrdersQuery({ status: 'InTransit' });

  const orders = data?.orders ?? [];
  const subject = (
    <LotSessionHeader
      title="Příjem podle objednávky"
      icon={ClipboardList}
      facts={isLoading ? 'Načítám…' : `${orders.length} objednávek v přepravě`}
    />
  );

  return (
    <ScanShell subject={subject}>
      {isLoading ? (
        <p className="text-sm text-neutral-gray dark:text-graphite-muted">Načítám objednávky…</p>
      ) : orders.length === 0 ? (
        <p className="text-sm text-neutral-gray dark:text-graphite-muted">Žádné objednávky v přepravě.</p>
      ) : (
        <div className="space-y-2">
          {orders.map((po) => (
            <Link
              key={po.id}
              to={`/terminal/lot-identification/po/${po.id}`}
              className="flex items-center gap-3 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-3 hover:border-primary-blue dark:hover:border-graphite-accent transition-colors"
            >
              <span className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-xl bg-violet-50 dark:bg-graphite-surface-2 text-violet-600 dark:text-violet-400">
                <ClipboardList className="h-5 w-5" />
              </span>
              <div className="flex-1 min-w-0">
                <p className="font-mono font-semibold text-neutral-slate dark:text-graphite-text truncate">
                  {po.orderNumber}
                </p>
                <p className="text-sm text-neutral-gray dark:text-graphite-muted truncate">{po.supplierName}</p>
              </div>
              <ChevronRight className="h-5 w-5 flex-shrink-0 text-neutral-gray dark:text-graphite-muted" />
            </Link>
          ))}
        </div>
      )}
    </ScanShell>
  );
};

export default PoPickStep;
