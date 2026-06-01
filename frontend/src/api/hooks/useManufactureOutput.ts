import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";

export interface ManufactureOutputResponse {
  months: ManufactureOutputMonth[];
}

export interface ManufactureOutputMonth {
  month: string; // Format: "YYYY-MM"
  totalOutput: number; // Weighted sum
  products: ProductContribution[];
  productionDetails: ProductionDetail[];
}

export interface ProductContribution {
  productCode: string;
  productName: string;
  quantity: number;
  difficulty: number;
  weightedValue: number;
}

export interface ProductionDetail {
  productCode: string;
  productName: string;
  date: string; // ISO date string
  amount: number;
  pricePerPiece: number;
  priceTotal: number;
  documentNumber: string;
}

export const useManufactureOutputQuery = (monthsBack: number = 13) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.manufactureOutput, monthsBack],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = `/api/manufacture-output?monthsBack=${monthsBack}`;
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: "GET",
        headers: {
          Accept: "application/json",
        },
      });

      if (!response.ok) {
        throw new Error(
          `Failed to fetch manufacture output: ${response.statusText}`,
        );
      }

      const data = await response.json();
      return data as ManufactureOutputResponse;
    },
    retry: 1,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

// Helper function to format month display
export const formatMonthDisplay = (monthStr: string): string => {
  const [year, month] = monthStr.split("-");
  const months = [
    "Leden",
    "Únor",
    "Březen",
    "Duben",
    "Květen",
    "Červen",
    "Červenec",
    "Srpen",
    "Září",
    "Říjen",
    "Listopad",
    "Prosinec",
  ];
  const monthIndex = parseInt(month, 10) - 1;
  return `${months[monthIndex]} ${year}`;
};

// Helper function to get month short name
export const getMonthShortName = (monthStr: string): string => {
  const [, month] = monthStr.split("-");
  const monthsShort = [
    "Led",
    "Úno",
    "Bře",
    "Dub",
    "Kvě",
    "Čer",
    "Čvc",
    "Srp",
    "Zář",
    "Říj",
    "Lis",
    "Pro",
  ];
  const monthIndex = parseInt(month, 10) - 1;
  return monthsShort[monthIndex];
};
