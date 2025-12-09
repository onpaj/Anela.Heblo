import React from "react";
import {
  BarChart,
  Download,
} from "lucide-react";

interface BankImportTabsProps {
  activeTab: "statistics" | "import";
  onTabChange: (tab: "statistics" | "import") => void;
  children: React.ReactNode;
}

const BankImportTabs: React.FC<BankImportTabsProps> = ({
  activeTab,
  onTabChange,
  children,
}) => {
  return (
    <div className="flex flex-col overflow-hidden">
      {/* Tab Navigation */}
      <div className="flex border-b border-gray-200 mb-6">
        <button
          onClick={() => onTabChange("statistics")}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === "statistics"
              ? "border-indigo-500 text-indigo-600"
              : "border-transparent text-gray-500 hover:text-gray-700"
          }`}
        >
          <BarChart className="h-4 w-4" />
          <span>Statistiky</span>
        </button>

        <button
          onClick={() => onTabChange("import")}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === "import"
              ? "border-indigo-500 text-indigo-600"
              : "border-transparent text-gray-500 hover:text-gray-700"
          }`}
        >
          <Download className="h-4 w-4" />
          <span>Import</span>
        </button>
      </div>

      {/* Tab Content */}
      <div className="flex-1 overflow-y-auto">
        {children}
      </div>
    </div>
  );
};

export default BankImportTabs;