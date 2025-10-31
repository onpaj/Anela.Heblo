import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// Types based on backend DTOs
export interface ClassificationRule {
  id: string;
  name: string;
  ruleTypeIdentifier: string;
  pattern: string;
  accountingPrescription: string;
  order: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
  updatedBy: string;
}

export interface ClassificationRuleType {
  identifier: string;
  displayName: string;
  description: string;
}

export interface CreateClassificationRuleRequest {
  name: string;
  ruleTypeIdentifier: string;
  pattern: string;
  accountingPrescription: string;
  isActive: boolean;
}

export interface UpdateClassificationRuleRequest {
  id: string;
  name: string;
  ruleTypeIdentifier: string;
  pattern: string;
  accountingPrescription: string;
  isActive: boolean;
}

export interface ClassifyInvoicesResponse {
  totalInvoicesProcessed: number;
  successfulClassifications: number;
  manualReviewRequired: number;
  errors: number;
  errorMessages: string[];
}

const CLASSIFICATION_QUERY_KEYS = {
  rules: ['invoice-classification', 'rules'] as const,
  ruleTypes: ['invoice-classification', 'rule-types'] as const,
  statistics: ['invoice-classification', 'statistics'] as const,
  history: ['invoice-classification', 'history'] as const,
} as const;

export function useClassificationRules(includeInactive = false) {
  return useQuery({
    queryKey: [...CLASSIFICATION_QUERY_KEYS.rules, includeInactive],
    queryFn: async () => {
      const apiClient = await getAuthenticatedApiClient();
      const url = `/api/invoiceclassification/rules?includeInactive=${includeInactive}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });
      
      if (!response.ok) {
        throw new Error(`Failed to fetch classification rules: ${response.statusText}`);
      }
      
      const data = await response.json();
      return data.rules as ClassificationRule[];
    },
  });
}

export function useCreateClassificationRule() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: async (request: CreateClassificationRuleRequest) => {
      const apiClient = await getAuthenticatedApiClient();
      const url = '/api/invoiceclassification/rules';
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });
      
      if (!response.ok) {
        throw new Error(`Failed to create classification rule: ${response.statusText}`);
      }
      
      const data = await response.json();
      return data.rule as ClassificationRule;
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
      const url = `/api/invoiceclassification/rules/${request.id}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      });
      
      if (!response.ok) {
        throw new Error(`Failed to update classification rule: ${response.statusText}`);
      }
      
      const data = await response.json();
      return data.rule as ClassificationRule;
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
      const url = `/api/invoiceclassification/rules/${ruleId}`;
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'DELETE',
        headers: {
          'Content-Type': 'application/json',
        },
      });
      
      if (!response.ok) {
        throw new Error(`Failed to delete classification rule: ${response.statusText}`);
      }
      
      return await response.json();
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
      const url = '/api/invoiceclassification/rules/reorder';
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ ruleIds }),
      });
      
      if (!response.ok) {
        throw new Error(`Failed to reorder classification rules: ${response.statusText}`);
      }
      
      return await response.json();
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
      const url = '/api/invoiceclassification/rule-types';
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: {
          'Content-Type': 'application/json',
        },
      });
      
      if (!response.ok) {
        throw new Error(`Failed to fetch classification rule types: ${response.statusText}`);
      }
      
      return await response.json() as ClassificationRuleType[];
    },
  });
}

export function useClassifyInvoices() {
  return useMutation({
    mutationFn: async (manualTrigger: boolean = true) => {
      const apiClient = await getAuthenticatedApiClient();
      const url = '/api/invoiceclassification/classify';
      const fullUrl = `${(apiClient as any).baseUrl}${url}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ manualTrigger }),
      });
      
      if (!response.ok) {
        throw new Error(`Failed to classify invoices: ${response.statusText}`);
      }
      
      return await response.json() as ClassifyInvoicesResponse;
    },
  });
}