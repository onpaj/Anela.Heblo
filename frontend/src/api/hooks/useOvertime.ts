import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  UpsertOvertimeEmployeeRequest,
  CreateAdjustmentRequest,
  SetStatementReviewedRequest,
  type OvertimeEmployeeDto,
  type AvailableLogetoPersonDto,
  type OvertimeStatementDto,
  type OvertimeAdjustmentDto,
  OvertimeAdjustmentType,
} from '../generated/api-client';

const overtimeKeys = {
  all: [...QUERY_KEYS.overtime] as const,
  employees: () => [...overtimeKeys.all, 'employees'] as const,
  month: (year: number, month: number) => [...overtimeKeys.all, 'month', year, month] as const,
};

export const useOvertimeEmployeesQuery = () =>
  useQuery({
    queryKey: overtimeKeys.employees(),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      return await client.overtime_GetEmployees();
    },
  });

export const useMonthlyStatementsQuery = (year: number, month: number) =>
  useQuery({
    queryKey: overtimeKeys.month(year, month),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      return await client.overtime_GetMonthlyStatements(year, month);
    },
  });

const useInvalidatingMutation = <TVariables,>(
  mutationFn: (variables: TVariables) => Promise<unknown>,
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: overtimeKeys.all });
    },
  });
};

export const useUpsertEmployeeMutation = () =>
  useInvalidatingMutation(async (employee: {
    personId: string; displayName: string; baselineHours: number; baselineDate: string; isActive: boolean;
  }) => {
    const client = getAuthenticatedApiClient();
    const request = new UpsertOvertimeEmployeeRequest({
      personId: employee.personId,
      displayName: employee.displayName,
      baselineHours: employee.baselineHours,
      baselineDate: new Date(employee.baselineDate),
      isActive: employee.isActive,
    });
    return await client.overtime_UpsertEmployee(request);
  });

export const useSetReviewedMutation = () =>
  useInvalidatingMutation(async (variables: { personId: string; year: number; month: number; isReviewed: boolean }) => {
    const client = getAuthenticatedApiClient();
    const request = new SetStatementReviewedRequest({
      personId: variables.personId,
      year: variables.year,
      month: variables.month,
      isReviewed: variables.isReviewed,
    });
    return await client.overtime_SetReviewed(variables.year, variables.month, request);
  });

export const useCreateAdjustmentMutation = () =>
  useInvalidatingMutation(async (variables: {
    personId: string; year: number; month: number; type: OvertimeAdjustmentType; hours: number; note: string;
  }) => {
    const client = getAuthenticatedApiClient();
    const request = new CreateAdjustmentRequest({
      personId: variables.personId,
      year: variables.year,
      month: variables.month,
      type: variables.type,
      hours: variables.hours,
      note: variables.note,
    });
    return await client.overtime_CreateAdjustment(request);
  });

export const useDeleteAdjustmentMutation = () =>
  useInvalidatingMutation(async (id: number) => {
    const client = getAuthenticatedApiClient();
    return await client.overtime_DeleteAdjustment(id);
  });

export const useCloseMonthMutation = () =>
  useInvalidatingMutation(async (variables: { year: number; month: number; force?: boolean }) => {
    const client = getAuthenticatedApiClient();
    return await client.overtime_CloseMonth(variables.year, variables.month, variables.force ?? false);
  });

export const usePublishReportMutation = () =>
  useInvalidatingMutation(async () => {
    const client = getAuthenticatedApiClient();
    return await client.overtime_PublishReport();
  });

interface ApiClientWithInternals {
  baseUrl: string;
  http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
}

const DEFAULT_EXPORT_FILENAME = 'Evidence-prescasu.xlsx';

/** Pulls the filename the backend sent via Content-Disposition; falls back to the
 * default export name (kept in sync with OvertimeOptions.ExportFileName) if absent. */
const extractFilename = (contentDisposition: string | null): string => {
  if (!contentDisposition) return DEFAULT_EXPORT_FILENAME;
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(contentDisposition);
  return match ? decodeURIComponent(match[1]) : DEFAULT_EXPORT_FILENAME;
};

/** Downloads the XLSX report via an authenticated fetch (a bare `window.open` navigation
 * can't carry the bearer token and would 401 against the [FeatureAuthorize]-gated endpoint —
 * see frontend/src/components/baleni/printLabelPdf.ts for the established pattern this follows).
 * Fetches the file as a blob, then triggers a programmatic download via an object URL. */
export const downloadOvertimeReport = async (): Promise<void> => {
  const client = getAuthenticatedApiClient(false) as unknown as ApiClientWithInternals;
  const url = `${client.baseUrl}/api/overtime/export`;
  const response = await client.http.fetch(url);
  if (!response.ok) {
    throw new Error(`Stažení reportu selhalo (${response.status})`);
  }

  const blob = await response.blob();
  const filename = extractFilename(response.headers.get('Content-Disposition'));
  const blobUrl = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = blobUrl;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(blobUrl);
};

export type {
  OvertimeEmployeeDto,
  AvailableLogetoPersonDto,
  OvertimeStatementDto,
  OvertimeAdjustmentDto,
};
export { OvertimeAdjustmentType };
