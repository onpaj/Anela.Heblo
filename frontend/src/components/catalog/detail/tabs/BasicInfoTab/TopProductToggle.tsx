import React from "react";
import { HelpCircle } from "lucide-react";
import { usePermissionsContext } from "../../../../../auth/PermissionsContext";
import { useSetTopProduct } from "../../../../../api/hooks/useTopProduct";

const CATALOG_WRITE_PERMISSION = "products.catalog.write";
const ND_LABEL = "ND";
const ND_MEANING = "ND = Nesmí dojít";

interface TopProductToggleProps {
  productCode: string;
  isTopProduct: boolean;
}

/** Checkbox that marks a product as a top product; the flag is stored in ABRA Flexi. */
function TopProductToggle({ productCode, isTopProduct }: TopProductToggleProps) {
  const { hasPermission } = usePermissionsContext();
  const setTopProduct = useSetTopProduct();

  const canEdit = hasPermission(CATALOG_WRITE_PERMISSION);
  // While saving, show the value being saved so the checkbox does not flip back until the refetch lands
  const isChecked = setTopProduct.isPending
    ? (setTopProduct.variables?.isTopProduct ?? isTopProduct)
    : isTopProduct;

  return (
    <div className="text-center">
      <label className="inline-flex items-center space-x-2 text-lg cursor-pointer">
        <input
          type="checkbox"
          checked={isChecked}
          disabled={!canEdit || setTopProduct.isPending}
          onChange={() =>
            setTopProduct.mutate({ productCode, isTopProduct: !isChecked })
          }
          className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500 disabled:cursor-not-allowed"
        />
        <span className="text-gray-900 dark:text-graphite-text">{ND_LABEL}</span>
      </label>
      <span
        role="img"
        aria-label={ND_MEANING}
        title={ND_MEANING}
        data-testid="nd-help"
        className="ml-1 inline-flex align-middle text-gray-400 cursor-help"
      >
        <HelpCircle aria-hidden="true" className="h-4 w-4" />
      </span>
      {setTopProduct.isError && (
        <p role="alert" className="mt-1 text-xs text-red-600 dark:text-red-400">
          Příznak se nepodařilo uložit. Zkuste to prosím znovu.
        </p>
      )}
    </div>
  );
}

export default TopProductToggle;
