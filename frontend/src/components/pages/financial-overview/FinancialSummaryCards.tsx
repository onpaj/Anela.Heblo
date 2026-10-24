import React from "react";
import {
  TrendingUp,
  TrendingDown,
  DollarSign,
  Calendar,
  Package,
  BarChart3,
  type LucideIcon,
} from "lucide-react";
import type { FinancialSummaryDto } from "../../../api/hooks/useFinancialOverview";
import { formatCurrency } from "./utils";

interface FinancialSummaryCardsProps {
  summary: FinancialSummaryDto;
  includeStockData: boolean;
}

interface CardConfig {
  key: string;
  icon: LucideIcon;
  iconClass: string;
  label: string;
  value: number;
  valueClass: string;
}

const NEUTRAL_VALUE_CLASS = "text-gray-900 dark:text-graphite-text";

const SummaryCard: React.FC<{ card: CardConfig }> = ({ card }) => {
  const Icon = card.icon;
  return (
    <div className="bg-white dark:bg-graphite-surface overflow-hidden shadow dark:shadow-soft-dark rounded-lg">
      <div className="p-3">
        <div className="flex items-center">
          <div className="flex-shrink-0">
            <Icon className={`h-5 w-5 ${card.iconClass}`} />
          </div>
          <div className="ml-3 w-0 flex-1">
            <dl>
              <dt className="text-xs font-medium text-gray-500 dark:text-graphite-muted truncate">
                {card.label}
              </dt>
              <dd className={`text-sm font-medium ${card.valueClass}`}>
                {formatCurrency(card.value)}
              </dd>
            </dl>
          </div>
        </div>
      </div>
    </div>
  );
};

export const FinancialSummaryCards: React.FC<FinancialSummaryCardsProps> = ({
  summary,
  includeStockData,
}) => {
  const balancePositive = summary.totalBalance >= 0;
  const averagePositive = summary.averageMonthlyBalance >= 0;

  const cards: CardConfig[] = [
    {
      key: "income",
      icon: TrendingUp,
      iconClass: "text-emerald-400",
      label: "Celkové příjmy",
      value: summary.totalIncome,
      valueClass: NEUTRAL_VALUE_CLASS,
    },
    {
      key: "expenses",
      icon: TrendingDown,
      iconClass: "text-red-400",
      label: "Celkové náklady",
      value: summary.totalExpenses,
      valueClass: NEUTRAL_VALUE_CLASS,
    },
    {
      key: "totalBalance",
      icon: DollarSign,
      iconClass: balancePositive ? "text-emerald-400" : "text-red-400",
      label: "Účetní bilance",
      value: summary.totalBalance,
      valueClass: balancePositive
        ? "text-emerald-600 dark:text-emerald-400"
        : "text-red-600 dark:text-red-400",
    },
    {
      key: "averageMonthlyBalance",
      icon: Calendar,
      iconClass: averagePositive ? "text-blue-400" : "text-red-400",
      label: "Průměrná měsíční bilance",
      value: summary.averageMonthlyBalance,
      valueClass: averagePositive
        ? "text-blue-600 dark:text-blue-400"
        : "text-red-600 dark:text-red-400",
    },
  ];

  if (includeStockData && summary.stockSummary) {
    const stockChange = summary.stockSummary.totalStockValueChange;
    const balanceWithStock = summary.stockSummary.totalBalanceWithStock;
    const stockPositive = !!stockChange && stockChange >= 0;
    const withStockPositive = !!balanceWithStock && balanceWithStock >= 0;

    cards.push(
      {
        key: "stockValueChange",
        icon: Package,
        iconClass: stockPositive ? "text-purple-400" : "text-orange-400",
        label: "Změna hodnoty skladu",
        value: stockChange || 0,
        valueClass: stockPositive
          ? "text-purple-600 dark:text-purple-400"
          : "text-orange-600 dark:text-amber-400",
      },
      {
        key: "balanceWithStock",
        icon: BarChart3,
        iconClass: withStockPositive ? "text-emerald-400" : "text-red-400",
        label: "Celková bilance vč. skladu",
        value: balanceWithStock || 0,
        valueClass: withStockPositive
          ? "text-emerald-600 dark:text-emerald-400"
          : "text-red-600 dark:text-red-400",
      },
    );
  }

  return (
    <div
      className={`grid grid-cols-2 md:grid-cols-2 ${includeStockData ? "xl:grid-cols-6" : "lg:grid-cols-4"} gap-4 mb-6`}
    >
      {cards.map((card) => (
        <SummaryCard key={card.key} card={card} />
      ))}
    </div>
  );
};
