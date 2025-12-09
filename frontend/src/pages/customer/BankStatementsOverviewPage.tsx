import React, { useState } from "react";
import { CreditCard, BarChart, Download } from "lucide-react";
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
      {/* Header with Tabs - Fixed */}
      <div className="flex-shrink-0 mb-3">
        <div className="flex items-center justify-between">
          <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-3">
            <CreditCard className="h-6 w-6 text-indigo-600" />
            Bankovní import
          </h1>
        </div>
        
        {/* Tab Navigation */}
        <div className="mt-3 border-b border-gray-200">
          <nav className="-mb-px flex space-x-8">
            <button
              onClick={() => setActiveTab("statistics")}
              className={`flex items-center gap-2 py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === "statistics"
                  ? "border-indigo-500 text-indigo-600"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <BarChart className="h-4 w-4" />
              Statistiky
            </button>
            <button
              onClick={() => setActiveTab("import")}
              className={`flex items-center gap-2 py-2 px-1 border-b-2 font-medium text-sm ${
                activeTab === "import"
                  ? "border-indigo-500 text-indigo-600"
                  : "border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300"
              }`}
            >
              <Download className="h-4 w-4" />
              Import
            </button>
          </nav>
        </div>
      </div>

      {/* Tab Content */}
      {activeTab === "statistics" ? (
        <StatisticsTab />
      ) : (
        <ImportTab />
      )}
    </div>
  );
};

export default BankStatementsOverviewPage;