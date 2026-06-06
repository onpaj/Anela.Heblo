import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient } from "../client";
import type {
  GetPermissionCatalogueResponse,
  GetGroupsResponse,
  GetGroupDetailResponse,
  GetUsersResponse,
  CreateGroupResponse,
  UpdateGroupResponse,
  DeleteGroupResponse,
  AssignUserGroupsResponse,
  SetUserActiveResponse,
} from "../generated/api-client";
import {
  CreateGroupRequest,
  UpdateGroupRequest,
  AssignUserGroupsRequest,
  SetUserActiveRequest,
} from "../generated/api-client";

const keys = {
  catalogue: ["authz", "catalogue"] as const,
  groups: ["authz", "groups"] as const,
  group: (id: string) => ["authz", "group", id] as const,
  users: ["authz", "users"] as const,
};

export const useCatalogue = () => {
  return useQuery({
    queryKey: keys.catalogue,
    queryFn: async (): Promise<GetPermissionCatalogueResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_Catalogue();
    },
  });
};

export const useGroups = () => {
  return useQuery({
    queryKey: keys.groups,
    queryFn: async (): Promise<GetGroupsResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetGroups();
    },
  });
};

export const useGroup = (id: string | null) => {
  return useQuery({
    queryKey: id ? keys.group(id) : keys.group(""),
    enabled: id !== null,
    queryFn: async (): Promise<GetGroupDetailResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetGroup(id!);
    },
  });
};

export const useUsers = () => {
  return useQuery({
    queryKey: keys.users,
    queryFn: async (): Promise<GetUsersResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetUsers();
    },
  });
};

export const useCreateGroup = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (
      request: CreateGroupRequest,
    ): Promise<CreateGroupResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_CreateGroup(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.groups });
    },
  });
};

export const useUpdateGroup = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: UpdateGroupRequest;
    }): Promise<UpdateGroupResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_UpdateGroup(id, request);
    },
    onSuccess: (_data, { id }) => {
      queryClient.invalidateQueries({ queryKey: keys.groups });
      queryClient.invalidateQueries({ queryKey: keys.group(id) });
    },
  });
};

export const useDeleteGroup = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string): Promise<DeleteGroupResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_DeleteGroup(id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.groups });
    },
  });
};

export const useAssignUserGroups = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: AssignUserGroupsRequest;
    }): Promise<AssignUserGroupsResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_AssignGroups(id, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};

export const useSetUserActive = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: SetUserActiveRequest;
    }): Promise<SetUserActiveResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_SetActive(id, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};

export type { CreateGroupRequest, UpdateGroupRequest, AssignUserGroupsRequest, SetUserActiveRequest };
