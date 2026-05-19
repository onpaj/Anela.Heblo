import { ImageOff } from 'lucide-react';
import type { PackingOrderItem } from '../../api/hooks/usePackingOrder';

/** Above this item count, photos are dropped and a dense list is shown instead. */
export const PHOTO_ITEM_LIMIT = 12;

interface PackingItemsProps {
  items: PackingOrderItem[];
}

function ItemQuantity({ quantity }: { quantity: number }) {
  return <span className="font-bold text-primary-blue shrink-0">{quantity}×</span>;
}

function PhotoGrid({ items }: PackingItemsProps) {
  return (
    <div
      data-testid="packing-items-grid"
      className="grid grid-cols-2 gap-2"
    >
      {items.map((item, index) => (
        <div
          key={`${item.name}-${index}`}
          className="flex items-center gap-2 bg-white border border-border-light rounded-lg p-2"
        >
          <div className="w-12 h-12 rounded-md bg-gray-100 flex items-center justify-center shrink-0 overflow-hidden">
            {item.imageUrl ? (
              <img src={item.imageUrl} alt={item.name} className="w-full h-full object-cover" />
            ) : (
              <ImageOff className="h-5 w-5 text-neutral-gray" />
            )}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm text-neutral-slate leading-tight">{item.name}</p>
            {item.setName && (
              <p className="text-xs text-neutral-gray">ze setu: {item.setName}</p>
            )}
          </div>
          <ItemQuantity quantity={item.quantity} />
        </div>
      ))}
    </div>
  );
}

function DenseList({ items }: PackingItemsProps) {
  return (
    <div
      data-testid="packing-items-list"
      className="columns-2 gap-4"
    >
      {items.map((item, index) => (
        <div
          key={`${item.name}-${index}`}
          className="flex items-center justify-between gap-2 text-sm py-1 border-b border-border-light break-inside-avoid"
        >
          <span className="text-neutral-slate truncate">{item.name}</span>
          <ItemQuantity quantity={item.quantity} />
        </div>
      ))}
    </div>
  );
}

function PackingItems({ items }: PackingItemsProps) {
  return items.length > PHOTO_ITEM_LIMIT ? (
    <DenseList items={items} />
  ) : (
    <PhotoGrid items={items} />
  );
}

export default PackingItems;
