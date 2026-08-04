import { useQuery } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  GetManufactureOutputResponse,
  ManufactureOutputMonth,
  ProductContribution,
  ProductionDetail,
} from "../generated/api-client";

export type {
  GetManufactureOutputResponse,
  ManufactureOutputMonth,
  ProductContribution,
  ProductionDetail,
};

export const useManufactureOutputQuery = (monthsBack: number = 13) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.manufactureOutput, monthsBack],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.manufactureOutput_GetManufactureOutput(monthsBack);
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
