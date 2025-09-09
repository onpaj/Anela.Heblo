import React from "react";
import { Loader2, TrendingUp } from "lucide-react";
import { CatalogItemDto } from "../../../../../api/hooks/useCatalog";
import {
  ManufactureCostDto,
  MarginHistoryDto,
  JournalEntryDto,
} from "../../../../../api/generated/api-client";
import MarginsSummary from "./MarginsSummary";
import MarginsChart from "./MarginsChart";

interface MarginsTabProps {
  item: CatalogItemDto | null;
  manufactureCostHistory: ManufactureCostDto[];
  marginHistory: MarginHistoryDto[];
  isLoading: boolean;
  journalEntries: JournalEntryDto[];
}

const MarginsTab: React.FC<MarginsTabProps> = ({
  item,
  manufactureCostHistory,
  marginHistory,
  isLoading,
  journalEntries,
}) => {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání dat o marži...</div>
        </div>
      </div>
    );
  }

  if (manufactureCostHistory.length === 0) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-center text-gray-500">
          <TrendingUp className="h-12 w-12 mx-auto mb-2 text-gray-300" />
          <p className="text-lg font-medium">
            Žádné údaje o nákladech na výrobu
          </p>
          <p className="text-sm">
            Pro tento produkt nejsou k dispozici historické náklady na výrobu
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <MarginsSummary
        item={item}
        manufactureCostHistory={manufactureCostHistory}
      />
      <MarginsChart
        manufactureCostHistory={manufactureCostHistory}
        marginHistory={marginHistory}
        journalEntries={journalEntries}
      />
    </div>
  );
};

export default MarginsTab;
