import React from "react";
import { Star } from "lucide-react";

const ND_LABEL = "ND – Nesmí dojít";

interface NdStarProps {
  isNd?: boolean | null;
  className?: string;
}

/** Small star shown next to a product name; renders nothing for non-top products. */
function NdStar({ isNd, className = "" }: NdStarProps) {
  if (!isNd) {
    return null;
  }

  return (
    <span
      data-testid="nd-star"
      role="img"
      aria-label={ND_LABEL}
      title={ND_LABEL}
      className={`inline-flex flex-shrink-0 align-middle ${className}`.trim()}
    >
      <Star aria-hidden="true" className="h-3.5 w-3.5 fill-amber-400 text-amber-500" />
    </span>
  );
}

export default NdStar;
