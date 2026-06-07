import { Link } from 'react-router-dom';
import { ChevronRight, ClipboardList } from 'lucide-react';
import { usePurchaseOrdersQuery } from '../../../api/hooks/usePurchaseOrders';

const PoPickStep = () => {
  const { data, isLoading } = usePurchaseOrdersQuery({ status: 'InTransit' });

  if (isLoading) {
    return <p className="text-sm text-neutral-gray">Načítám objednávky…</p>;
  }

  if (!data?.orders?.length) {
    return <p className="text-sm text-neutral-gray">Žádné objednávky v přepravě.</p>;
  }

  return (
    <div className="space-y-3 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">Vyberte objednávku</h2>
      {data.orders.map((po) => (
        <Link
          key={po.id}
          to={`/terminal/lot-identification/po/${po.id}`}
          className="flex items-center gap-3 bg-white border border-border-light rounded-xl p-3 hover:border-primary-blue"
        >
          <ClipboardList className="h-5 w-5 text-primary-blue flex-shrink-0" />
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-neutral-slate">{po.orderNumber}</p>
            <p className="text-sm text-neutral-gray">{po.supplierName}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray" />
        </Link>
      ))}
    </div>
  );
};

export default PoPickStep;
