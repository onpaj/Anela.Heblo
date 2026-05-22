import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';

export type FolderStatus = 'Found' | 'NotFound' | 'MultipleMatches';

export interface CatalogDocumentDto {
  name: string;
  webUrl: string;
  sizeBytes: number;
  modifiedAt: string;
}

export interface MaterialDocumentTypeDto {
  code: string;
  label: string;
  lotRequired: boolean;
}

export interface ListCatalogDocumentsResponse {
  success: boolean;
  folderStatus: FolderStatus;
  expectedPrefix: string;
  basePath: string;
  files: CatalogDocumentDto[];
}

export interface GetMaterialDocumentTypesResponse {
  success: boolean;
  documentTypes: MaterialDocumentTypeDto[];
}

export interface UploadDocumentResponse {
  success: boolean;
  uploadedFilename: string;
  errorCode?: number;
  params?: Record<string, string>;
}

export interface UploadMaterialDocumentParams {
  productCode: string;
  file: File;
  documentTypeCode: string;
  lot: string;
  commonName: string;
  uploadAsIs: boolean;
}

export interface UploadPifDocumentParams {
  productCode: string;
  file: File;
}

// Single cast boundary for the private ApiClient internals
function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  const apiClient = getAuthenticatedApiClient();
  const baseUrl = (apiClient as any).baseUrl as string;
  return (apiClient as any).http.fetch(`${baseUrl}${path}`, init) as Promise<Response>;
}

const catalogDocumentsKeys = {
  materialDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'materials', productCode] as const,
  pifDocuments: (productCode: string) =>
    [...QUERY_KEYS.catalogDocuments, 'pif', productCode] as const,
  materialDocumentTypes: () =>
    [...QUERY_KEYS.catalogDocuments, 'material-document-types'] as const,
};

export function useMaterialDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocuments(productCode),
    queryFn: async (): Promise<ListCatalogDocumentsResponse> => {
      const response = await apiFetch(
        `/api/catalog-documents/materials/${encodeURIComponent(productCode)}`,
        { method: 'GET', headers: { Accept: 'application/json' } },
      );
      if (!response.ok) throw new Error(`Failed to fetch material documents for ${productCode}: ${response.status}`);
      return response.json();
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function usePifDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.pifDocuments(productCode),
    queryFn: async (): Promise<ListCatalogDocumentsResponse> => {
      const response = await apiFetch(
        `/api/catalog-documents/pif/${encodeURIComponent(productCode)}`,
        { method: 'GET', headers: { Accept: 'application/json' } },
      );
      if (!response.ok) throw new Error(`Failed to fetch PIF documents for ${productCode}: ${response.status}`);
      return response.json();
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function useMaterialDocumentTypes() {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocumentTypes(),
    queryFn: async (): Promise<GetMaterialDocumentTypesResponse> => {
      const response = await apiFetch(
        '/api/catalog-documents/material-document-types',
        { method: 'GET', headers: { Accept: 'application/json' } },
      );
      if (!response.ok) throw new Error(`Failed to fetch material document types: ${response.status}`);
      return response.json();
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useUploadMaterialDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (params: UploadMaterialDocumentParams): Promise<UploadDocumentResponse> => {
      const formData = new FormData();
      formData.append('file', params.file);
      formData.append('documentTypeCode', params.documentTypeCode);
      formData.append('lot', params.lot);
      formData.append('commonName', params.commonName);
      formData.append('uploadAsIs', String(params.uploadAsIs));
      const response = await apiFetch(
        `/api/catalog-documents/materials/${encodeURIComponent(params.productCode)}`,
        { method: 'POST', body: formData },
      );
      if (!response.ok) throw new Error(`Failed to upload material document for ${params.productCode}: ${response.status}`);
      return response.json();
    },
    retry: 0,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.materialDocuments(variables.productCode),
      });
    },
  });
}

export function useUploadPifDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (params: UploadPifDocumentParams): Promise<UploadDocumentResponse> => {
      const formData = new FormData();
      formData.append('file', params.file);
      const response = await apiFetch(
        `/api/catalog-documents/pif/${encodeURIComponent(params.productCode)}`,
        { method: 'POST', body: formData },
      );
      if (!response.ok) throw new Error(`Failed to upload PIF document for ${params.productCode}: ${response.status}`);
      return response.json();
    },
    retry: 0,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.pifDocuments(variables.productCode),
      });
    },
  });
}
