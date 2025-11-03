import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import {
  ClassificationRuleDto,
  ClassificationRuleTypeDto,
  AccountingTemplateDto,
  ClassificationHistoryDto,
  CreateClassificationRuleRequest,
  UpdateClassificationRuleRequest,
  ClassifyInvoicesRequest,
  ClassifyInvoicesResponse,
  ReorderClassificationRulesRequest,
  GetInvoiceDetailsResponse,
  GetClassificationHistoryResponse
} from '../generated/api-client';

// Re-export types from generated client for easier usage
export type ClassificationRule = ClassificationRuleDto;
export type ClassificationRuleType = ClassificationRuleTypeDto;
export type AccountingTemplate = AccountingTemplateDto;
export type ClassificationHistoryItem = ClassificationHistoryDto;
export type ClassificationHistoryResponse = GetClassificationHistoryResponse;

// Keep these interfaces for compatibility
export { CreateClassificationRuleRequest, UpdateClassificationRuleRequest, ClassifyInvoicesResponse };

const CLASSIFICATION_QUERY_KEYS = {
  rules: ['invoice-classification', 'rules'] as const,
  ruleTypes: ['invoice-classification', 'rule-types'] as const,
  accountingTemplates: ['invoice-classification', 'accounting-templates'] as const,
  history: ['invoice-classification', 'history'] as const,
  statistics: ['invoice-classification', 'statistics'] as const,
} as const;

export function useClassificationRules(includeInactive = false) {
  return useQuery({
    queryKey: [...CLASSIFICATION_QUERY_KEYS.rules, includeInactive],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const response = await apiClient.invoiceClassification_GetRules(includeInactive);
      return response.rules || [];
    },
  });
}

export function useCreateClassificationRule() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreateClassificationRuleRequest) => {
      const apiClient = await getAuthenticatedApiClient();
      const response = await apiClient.invoiceClassification_CreateRule(request);
      return response.rule!;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CLASSIFICATION_QUERY_KEYS.rules });
    },
  });
}

export function useUpdateClassificationRule() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: UpdateClassificationRuleRequest) => {
      const apiClient = await getAuthenticatedApiClient();
      const response = await apiClient.invoiceClassification_UpdateRule(request.id!, request);
      return response.rule!;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CLASSIFICATION_QUERY_KEYS.rules });
    },
  });
}

export function useDeleteClassificationRule() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (ruleId: string) => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.invoiceClassification_DeleteRule(ruleId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CLASSIFICATION_QUERY_KEYS.rules });
    },
  });
}

export function useReorderClassificationRules() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (ruleIds: string[]) => {
      const apiClient = await getAuthenticatedApiClient();
      const request = new ReorderClassificationRulesRequest({ ruleIds });
      return await apiClient.invoiceClassification_ReorderRules(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CLASSIFICATION_QUERY_KEYS.rules });
    },
  });
}

export function useClassificationRuleTypes() {
  return useQuery({
    queryKey: CLASSIFICATION_QUERY_KEYS.ruleTypes,
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.invoiceClassification_GetAvailableRuleTypes();
    },
  });
}

export function useAccountingTemplates() {
  return useQuery({
    queryKey: CLASSIFICATION_QUERY_KEYS.accountingTemplates,
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const response = await apiClient.invoiceClassification_GetAccountingTemplates();
      return response.templates || [];
    },
  });
}

export function useClassifyInvoices() {
  return useMutation({
    mutationFn: async (manualTrigger: boolean = true) => {
      const apiClient = await getAuthenticatedApiClient();
      const request = new ClassifyInvoicesRequest({ manualTrigger });
      return await apiClient.invoiceClassification_ClassifyInvoices(request);
    },
  });
}

export function useClassificationHistory(
  page: number = 1,
  pageSize: number = 20,
  fromDate?: Date,
  toDate?: Date,
  invoiceNumber?: string,
  companyName?: string
) {
  return useQuery({
    queryKey: [...CLASSIFICATION_QUERY_KEYS.history, page, pageSize, fromDate, toDate, invoiceNumber, companyName],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.invoiceClassification_GetClassificationHistory(
        page,
        pageSize,
        fromDate || null,
        toDate || null,
        invoiceNumber || null,
        companyName || null
      );
    },
  });
}

// Re-export types from generated client
export { GetInvoiceDetailsResponse };

export function useClassifySingleInvoice() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (invoiceId: string) => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.invoiceClassification_ClassifySingleInvoice(invoiceId);
    },
    onSuccess: () => {
      // Invalidate history to refresh the list
      queryClient.invalidateQueries({ queryKey: CLASSIFICATION_QUERY_KEYS.history });
    },
  });
}

export function useInvoiceDetails(invoiceId: string, enabled: boolean = true) {
  return useQuery({
    queryKey: [...CLASSIFICATION_QUERY_KEYS.history, 'invoice', invoiceId],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      return await apiClient.invoiceClassification_GetInvoiceDetails(invoiceId);
    },
    enabled: enabled && !!invoiceId,
  });
}