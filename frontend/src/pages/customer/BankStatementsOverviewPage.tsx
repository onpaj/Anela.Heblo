import React, { useState } from "react";
import { CreditCard } from "lucide-react";
import BankImportTabs from "../../components/customer/BankImportTabs";
import StatisticsTab from "../../components/customer/tabs/StatisticsTab";
import ImportTab from "../../components/customer/tabs/ImportTab";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const BankStatementsOverviewPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<"statistics" | "import">("statistics");

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-3">
              <CreditCard className="h-6 w-6 text-indigo-600" />
              Bankovní import
            </h1>
            <p className="text-gray-600 mt-1 text-sm">
              Statistiky a import bankovních výpisů
            </p>
          </div>
        </div>
      </div>

      {/* Tabbed Content */}
      <div className="flex-1 overflow-hidden">
        <BankImportTabs 
          activeTab={activeTab} 
          onTabChange={setActiveTab}
        >
          {activeTab === "statistics" ? <StatisticsTab /> : <ImportTab />}
        </BankImportTabs>
      </div>
    </div>
  );
};

export default BankStatementsOverviewPage;