import React from "react";
import { Star } from "lucide-react";

const TOP_PRODUCT_LABEL = "Top produkt";

interface TopProductStarProps {
  isTopProduct?: boolean | null;
  className?: string;
}

/** Small star shown next to a product name; renders nothing for non-top products. */
function TopProductStar({ isTopProduct, className = "" }: TopProductStarProps) {
  if (!isTopProduct) {
    return null;
  }

  return (
    <span
      data-testid="top-product-star"
      role="img"
      aria-label={TOP_PRODUCT_LABEL}
      title={TOP_PRODUCT_LABEL}
      className={`inline-flex flex-shrink-0 align-middle ${className}`.trim()}
    >
      <Star aria-hidden="true" className="h-3.5 w-3.5 fill-amber-400 text-amber-500" />
    </span>
  );
}

export default TopProductStar;
