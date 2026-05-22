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
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/catalog-documents/materials/${encodeURIComponent(productCode)}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
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
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/catalog-documents/pif/${encodeURIComponent(productCode)}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
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
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/catalog-documents/material-document-types`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useUploadMaterialDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (params: UploadMaterialDocumentParams): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/catalog-documents/materials/${encodeURIComponent(params.productCode)}`;
      const formData = new FormData();
      formData.append('file', params.file);
      formData.append('documentTypeCode', params.documentTypeCode);
      formData.append('lot', params.lot);
      formData.append('commonName', params.commonName);
      formData.append('uploadAsIs', String(params.uploadAsIs));
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        body: formData,
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
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
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/catalog-documents/pif/${encodeURIComponent(params.productCode)}`;
      const formData = new FormData();
      formData.append('file', params.file);
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        body: formData,
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
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
