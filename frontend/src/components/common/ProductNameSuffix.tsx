import React from "react";
import { normalizeProductNameSuffix } from "../../utils/productName";

interface ProductNameSuffixProps {
  suffix?: string | null;
  className?: string;
}

/** Muted second line under a product name; renders nothing when the suffix is blank. */
const ProductNameSuffix: React.FC<ProductNameSuffixProps> = ({
  suffix,
  className = "",
}) => {
  const normalizedSuffix = normalizeProductNameSuffix(suffix);
  if (!normalizedSuffix) {
    return null;
  }

  return (
    <span
      data-testid="product-name-suffix"
      className={`block text-xs text-gray-500 dark:text-graphite-muted truncate ${className}`.trim()}
    >
      {normalizedSuffix}
    </span>
  );
};

export default ProductNameSuffix;
