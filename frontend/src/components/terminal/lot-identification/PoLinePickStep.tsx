import { Link, useParams } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';
import { usePurchaseOrderDetailQuery } from '../../../api/hooks/usePurchaseOrders';

const PoLinePickStep = () => {
  const { id } = useParams<{ id: string }>();
  const poId = id ? parseInt(id, 10) : 0;
  const { data, isLoading } = usePurchaseOrderDetailQuery(poId);

  if (isLoading || !data) {
    return <p className="text-sm text-neutral-gray">Načítám…</p>;
  }

  return (
    <div className="space-y-3 pt-2">
      <h2 className="text-lg font-semibold text-neutral-slate">
        {data.orderNumber} — položky
      </h2>
      {(data.lines ?? []).map((line) => (
        <Link
          key={line.id}
          to={`/terminal/lot-identification/po/${poId}/line/${line.id}/lot`}
          className="flex items-center gap-3 bg-white border border-border-light rounded-xl p-3 hover:border-primary-blue"
        >
          <div className="flex-1 min-w-0">
            <p className="font-semibold text-neutral-slate">{line.materialName}</p>
            <p className="text-sm text-neutral-gray">{line.materialId}</p>
          </div>
          <ChevronRight className="h-5 w-5 text-neutral-gray" />
        </Link>
      ))}
    </div>
  );
};

export default PoLinePickStep;
