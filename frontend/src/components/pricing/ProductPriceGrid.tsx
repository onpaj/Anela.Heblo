import React, { useEffect, useState } from "react";
import { useSetProductPrice } from "../../api/hooks/useProductPricing";
// Enums/DTOs are imported from the generated client directly (not from the hooks
// module) so this component keeps working when tests mock ../../api/hooks/useProductPricing.
import { ProductPriceDto, PriceSyncStatus, PriceSyncTarget } from "../../api/generated/api-client";
import { formatCurrency } from "../../utils/formatters";
import PriceConflictBanner from "./PriceConflictBanner";

interface ProductPriceGridProps {
  prices: ProductPriceDto[];
}

const STATUS_LABELS: Record<PriceSyncStatus, string> = {
  [PriceSyncStatus.InSync]: "Synchronizováno",
  [PriceSyncStatus.Pending]: "Čeká na synchronizaci",
  [PriceSyncStatus.Conflict]: "Konflikt",
  [PriceSyncStatus.Failed]: "Chyba",
};

const STATUS_STYLES: Record<PriceSyncStatus, string> = {
  [PriceSyncStatus.InSync]: "bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300",
  [PriceSyncStatus.Pending]: "bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted",
  [PriceSyncStatus.Conflict]: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300",
  [PriceSyncStatus.Failed]: "bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300",
};

interface SyncStatusChipProps {
  status: PriceSyncStatus;
  testId: string;
}

const SyncStatusChip: React.FC<SyncStatusChipProps> = ({ status, testId }) => (
  <span
    data-testid={testId}
    className={`inline-flex px-2 py-1 text-xs font-semibold rounded-full ${STATUS_STYLES[status]}`}
  >
    {STATUS_LABELS[status]}
  </span>
);

interface EditablePriceCellProps {
  productCode: string;
  priceWithVat: number;
}

// Accepts the Czech decimal comma and rejects anything the backend validator would
// refuse (`GreaterThan(0)`). `Number("")` is 0, not NaN, so an empty field has to be
// rejected explicitly or a cleared input would submit a zero price.
const parsePrice = (raw: string): number | null => {
  const normalized = raw.trim().replace(",", ".");
  if (normalized === "") return null;
  const parsed = Number(normalized);
  if (!Number.isFinite(parsed) || parsed <= 0) return null;
  return parsed;
};

const EditablePriceCell: React.FC<EditablePriceCellProps> = ({ productCode, priceWithVat }) => {
  const setPrice = useSetProductPrice();
  const [value, setValue] = useState(String(priceWithVat));

  // Keep the input aligned with the latest server value (e.g. after a successful save).
  useEffect(() => {
    setValue(String(priceWithVat));
  }, [priceWithVat]);

  const handleBlur = () => {
    const parsed = parsePrice(value);
    if (parsed === null) {
      setValue(String(priceWithVat));
      return;
    }
    if (parsed === priceWithVat) return;
    setPrice.mutate({ productCode, priceWithVat: parsed });
  };

  return (
    <div className="flex flex-col items-end gap-1">
      <input
        type="text"
        inputMode="decimal"
        aria-label={`Cena s DPH pro ${productCode}`}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onBlur={handleBlur}
        disabled={setPrice.isPending}
        className="w-24 px-2 py-1 text-sm text-right border border-gray-300 dark:border-graphite-border rounded-md bg-white dark:bg-graphite-surface text-gray-900 dark:text-graphite-text disabled:opacity-50"
      />
      {setPrice.isError && (
        <span
          data-testid={`set-price-error-${productCode}`}
          className="text-xs text-red-600 dark:text-red-400"
        >
          {setPrice.error?.message ?? "Cenu se nepodařilo uložit."}
        </span>
      )}
    </div>
  );
};

const GRID_COLUMN_COUNT = 7;

const ProductPriceGrid: React.FC<ProductPriceGridProps> = ({ prices }) => {
  return (
    <div className="bg-white dark:bg-graphite-surface rounded-lg shadow dark:shadow-soft-dark overflow-hidden">
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
          <thead className="bg-gray-50 dark:bg-graphite-surface-2">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Kód
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Název
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Cena s DPH
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Cena bez DPH
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                DPH
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Shoptet
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                Flexi
              </th>
            </tr>
          </thead>
          <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
            {prices.length === 0 ? (
              <tr>
                <td
                  colSpan={GRID_COLUMN_COUNT}
                  className="px-6 py-12 text-center text-gray-500 dark:text-graphite-muted"
                >
                  Žádné produkty k zobrazení.
                </td>
              </tr>
            ) : (
              prices.map((price) => (
                <React.Fragment key={price.productCode}>
                  <tr className="hover:bg-gray-50 dark:hover:bg-white/5">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900 dark:text-graphite-text">
                      {price.productCode}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-graphite-text">
                      {price.productName}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right">
                      <EditablePriceCell productCode={price.productCode!} priceWithVat={price.priceWithVat!} />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
                      {formatCurrency(price.priceWithoutVat ?? 0)}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-gray-900 dark:text-graphite-text">
                      {price.vatRate}%
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <SyncStatusChip status={price.shoptetStatus!} testId="sync-status-shoptet" />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <SyncStatusChip status={price.flexiStatus!} testId="sync-status-flexi" />
                    </td>
                  </tr>
                  {price.shoptetStatus === PriceSyncStatus.Conflict && (
                    <tr>
                      <td colSpan={GRID_COLUMN_COUNT} className="px-6 pb-3">
                        <PriceConflictBanner
                          productCode={price.productCode!}
                          target={PriceSyncTarget.Shoptet}
                          hebloPrice={price.priceWithVat!}
                          remotePrice={price.shoptetRemoteValue ?? null}
                        />
                      </td>
                    </tr>
                  )}
                  {price.flexiStatus === PriceSyncStatus.Conflict && (
                    <tr>
                      <td colSpan={GRID_COLUMN_COUNT} className="px-6 pb-3">
                        <PriceConflictBanner
                          productCode={price.productCode!}
                          target={PriceSyncTarget.Flexi}
                          hebloPrice={price.priceWithVat!}
                          remotePrice={price.flexiRemoteValue ?? null}
                        />
                      </td>
                    </tr>
                  )}
                </React.Fragment>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default ProductPriceGrid;
