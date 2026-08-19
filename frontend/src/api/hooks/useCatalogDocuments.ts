import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  type ListCatalogDocumentsResponse,
  type GetMaterialDocumentTypesResponse,
  type UploadDocumentResponse,
  type FileParameter,
} from '../generated/api-client';

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
    queryFn: (): Promise<ListCatalogDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalogDocuments_ListMaterialDocuments(productCode);
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function usePifDocuments(productCode: string) {
  return useQuery({
    queryKey: catalogDocumentsKeys.pifDocuments(productCode),
    queryFn: (): Promise<ListCatalogDocumentsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalogDocuments_ListPifDocuments(productCode);
    },
    staleTime: 30_000,
    enabled: !!productCode,
  });
}

export function useMaterialDocumentTypes() {
  return useQuery({
    queryKey: catalogDocumentsKeys.materialDocumentTypes(),
    queryFn: (): Promise<GetMaterialDocumentTypesResponse> => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.catalogDocuments_GetMaterialDocumentTypes();
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useUploadMaterialDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (params: UploadMaterialDocumentParams): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const file: FileParameter = { data: params.file, fileName: params.file.name };
      return apiClient.catalogDocuments_UploadMaterialDocument(
        params.productCode,
        file,
        params.documentTypeCode,
        params.lot,
        params.commonName,
        params.uploadAsIs,
      );
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
    mutationFn: (params: UploadPifDocumentParams): Promise<UploadDocumentResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const file: FileParameter = { data: params.file, fileName: params.file.name };
      return apiClient.catalogDocuments_UploadPifDocument(params.productCode, file);
    },
    retry: 0,
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: catalogDocumentsKeys.pifDocuments(variables.productCode),
      });
    },
  });
}
