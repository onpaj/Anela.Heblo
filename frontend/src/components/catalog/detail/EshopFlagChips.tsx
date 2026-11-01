import React from "react";
import { EshopFlagDto } from "../../../api/generated/api-client";

interface EshopFlagChipsProps {
  flags?: EshopFlagDto[] | null;
}

const EshopFlagChips: React.FC<EshopFlagChipsProps> = ({ flags }) => {
  if (!flags || flags.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-1 mt-1">
      {flags.map((flag, index) => (
        <span
          key={flag.code ?? `${flag.title}-${index}`}
          data-testid="eshop-flag-chip"
          className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-600 dark:bg-graphite-surface-2 dark:text-graphite-muted"
        >
          {flag.title}
        </span>
      ))}
    </div>
  );
};

export default EshopFlagChips;
