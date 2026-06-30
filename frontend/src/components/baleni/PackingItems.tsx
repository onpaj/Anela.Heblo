import { ImageOff } from 'lucide-react';
import type { PackingOrderItem } from '../../api/hooks/useScanPackingOrder';

/** Above this item count, photos are dropped and a dense list is shown instead. */
export const PHOTO_ITEM_LIMIT = 8;

interface PackingItemsProps {
  items: PackingOrderItem[];
}

function ItemQuantity({ quantity, large }: { quantity: number; large?: boolean }) {
  return (
    <span className={`font-bold text-primary-blue dark:text-graphite-accent shrink-0 ${large ? 'text-xl' : ''}`}>
      {quantity}×
    </span>
  );
}

function PhotoGrid({ items }: PackingItemsProps) {
  return (
    <div
      data-testid="packing-items-grid"
      className="grid grid-cols-1 gap-3"
    >
      {items.map((item, index) => (
        <div
          key={`${item.name}-${index}`}
          className="flex items-center gap-3 bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-lg p-3"
        >
          <div className="w-16 h-16 rounded-md bg-gray-100 dark:bg-graphite-surface-2 flex items-center justify-center shrink-0 overflow-hidden">
            {item.imageUrl ? (
              <img src={item.imageUrl} alt={item.name} className="w-full h-full object-cover" />
            ) : (
              <ImageOff className="h-6 w-6 text-neutral-gray dark:text-graphite-muted" />
            )}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-base font-medium text-neutral-slate dark:text-graphite-text leading-snug">{item.name}</p>
            {item.setName && (
              <p className="text-sm text-neutral-gray dark:text-graphite-muted">ze setu: {item.setName}</p>
            )}
          </div>
          <ItemQuantity quantity={item.quantity} large />
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
          className="flex items-center justify-between gap-2 text-sm py-1 border-b border-border-light dark:border-graphite-border break-inside-avoid"
        >
          <span className="text-neutral-slate dark:text-graphite-text truncate">{item.name}</span>
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
