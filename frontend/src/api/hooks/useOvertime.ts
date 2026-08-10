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

/** Absolute URL for the XLSX download (opened via window.open — the generated
 * client can't stream files; absolute per CLAUDE.md, relative would hit port 3001). */
export const downloadReportUrl = (): string => {
  const client = getAuthenticatedApiClient() as any;
  return `${client.baseUrl}/api/overtime/export`;
};

export type {
  OvertimeEmployeeDto,
  AvailableLogetoPersonDto,
  OvertimeStatementDto,
  OvertimeAdjustmentDto,
};
export { OvertimeAdjustmentType };
