import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  AddPhotoTagBody,
  BulkAddPhotoTagBody,
  BulkAddPhotoTagByIdsBody,
  CreateTagBody,
  GetPhotosResponse,
  PhotoDto,
  RetagPhotosBody,
  TagDto,
  TagWithCountDto,
} from "../generated/api-client";

export type { GetPhotosResponse, PhotoDto, TagDto, TagWithCountDto };

// ---- Types ----------------------------------------------------------------

export interface GetPhotosParams {
  tags?: string[];
  search?: string;
  useRegex?: boolean;
  withoutTags?: boolean;
  page?: number;
  pageSize?: number;
}

// ---- Hooks ----------------------------------------------------------------

export const usePhotos = (params: GetPhotosParams = {}) => {
  return useQuery<GetPhotosResponse>({
    queryKey: [...QUERY_KEYS.photobank, "photos", params],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.photobank_GetPhotos(
        params.tags,
        params.search,
        params.useRegex,
        params.withoutTags,
        params.page,
        params.pageSize,
      );
    },
  });
};

export const usePhotoTags = () => {
  return useQuery<TagWithCountDto[]>({
    queryKey: [...QUERY_KEYS.photobank, "tags"],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.photobank_GetTags();
      return response.tags ?? [];
    },
  });
};

export const useAddPhotoTag = (photoId: number) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (tagName: string) => {
      const apiClient = getAuthenticatedApiClient();
      await apiClient.photobank_AddPhotoTag(photoId, new AddPhotoTagBody({ tagName }));
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

export const useRemovePhotoTag = (photoId: number) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (tagId: number) => {
      const apiClient = getAuthenticatedApiClient();
      await apiClient.photobank_RemovePhotoTag(photoId, tagId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

// ---- Tag CRUD ---------------------------------------------------------------

export const useCreateTag = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (name: string) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.photobank_CreateTag(new CreateTagBody({ name }));
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

export const useDeleteTag = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (tagId: number) => {
      const apiClient = getAuthenticatedApiClient();
      return apiClient.photobank_DeleteTag(tagId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

// ---- Bulk tag ---------------------------------------------------------------

export interface BulkAddPhotoTagParams {
  tags?: string[];
  search?: string;
  tagName: string;
}

export interface BulkAddPhotoTagResult {
  success: boolean;
  errorCode?: number;
  params?: Record<string, string>;
  tagId?: number;
  tagName?: string;
  addedCount?: number;
  alreadyTaggedCount?: number;
}

// ---- Bulk tag by IDs --------------------------------------------------------

export interface BulkAddPhotoTagByIdsParams {
  photoIds: number[];
  tagName: string;
}

export const useBulkAddPhotoTagByIds = () => {
  const queryClient = useQueryClient();
  return useMutation<void, Error, BulkAddPhotoTagByIdsParams>({
    mutationFn: async (params) => {
      const apiClient = getAuthenticatedApiClient();
      await apiClient.photobank_BulkAddPhotoTagByIds(
        new BulkAddPhotoTagByIdsBody({ photoIds: params.photoIds, tagName: params.tagName }),
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

// ---- Auto-tag ---------------------------------------------------------------

export interface RetagPhotosRequest {
  photoIds: number[];
  clearExistingAiTags: boolean;
}

export const useRetagPhotos = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (request: RetagPhotosRequest) => {
      const apiClient = getAuthenticatedApiClient();
      await apiClient.photobank_RetagPhotos(
        new RetagPhotosBody({
          photoIds: request.photoIds,
          clearExistingAiTags: request.clearExistingAiTags,
        }),
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
    },
  });
};

export const useBulkAddPhotoTag = () => {
  const queryClient = useQueryClient();
  return useMutation<BulkAddPhotoTagResult, Error, BulkAddPhotoTagParams>({
    mutationFn: async (params) => {
      const apiClient = getAuthenticatedApiClient();
      try {
        const response = await apiClient.photobank_BulkAddPhotoTag(
          new BulkAddPhotoTagBody({
            tags: params.tags,
            search: params.search,
            tagName: params.tagName,
          }),
        );
        return {
          success: true,
          tagId: response.tagId,
          tagName: response.tagName,
          addedCount: response.addedCount,
          alreadyTaggedCount: response.alreadyTaggedCount,
        };
      } catch (e) {
        // The generated client throws (rather than returns) the 400 business-outcome body,
        // parsing it as ProblemDetails. Its `[key: string]: any` index signature plus
        // copy-all-properties init() means the real BulkAddPhotoTagResponse fields
        // (success/errorCode/params/...) still land on the thrown object at runtime, just
        // untyped. A 403 (empty body) yields no `success` property at all, so it falls
        // through to the rethrow below rather than matching `success === false`.
        const err = e as Partial<BulkAddPhotoTagResult> & { success?: boolean };
        if (err && err.success === false && typeof err.errorCode === "number") {
          return { success: false, errorCode: err.errorCode, params: err.params };
        }
        throw e;
      }
    },
    onSuccess: (data) => {
      if (data.success) {
        queryClient.invalidateQueries({ queryKey: QUERY_KEYS.photobank });
      }
    },
  });
};
