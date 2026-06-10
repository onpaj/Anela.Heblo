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
  GetEntraAccessUsersResponse,
  AddGroupMemberResponse,
  GetUserEffectivePermissionsResponse,
  CreateLocalUserResponse,
  SetUserCanPackResponse,
  UpdateUserResponse,
} from "../generated/api-client";
import {
  CreateGroupRequest,
  UpdateGroupRequest,
  AssignUserGroupsRequest,
  SetUserActiveRequest,
  AddGroupMemberRequest,
  CreateLocalUserRequest,
  SetUserCanPackRequest,
  UpdateUserRequest,
} from "../generated/api-client";

const keys = {
  catalogue: ["authz", "catalogue"] as const,
  groups: ["authz", "groups"] as const,
  group: (id: string) => ["authz", "group", id] as const,
  users: ["authz", "users"] as const,
  entraUsers: ["authz", "entra-users"] as const,
  userPermissionsPrefix: ["authz", "user-permissions"] as const,
  userPermissions: (id: string) => ["authz", "user-permissions", id] as const,
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

export const useUserPermissions = (id: string | null) => {
  return useQuery({
    queryKey: id ? keys.userPermissions(id) : keys.userPermissions(""),
    enabled: id !== null,
    queryFn: async (): Promise<GetUserEffectivePermissionsResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetUserPermissions(id!);
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
      queryClient.invalidateQueries({ queryKey: keys.userPermissionsPrefix, exact: false });
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
      queryClient.invalidateQueries({ queryKey: keys.userPermissionsPrefix, exact: false });
    },
  });
};

export const useUpdateUser = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      request,
    }: {
      id: string;
      request: UpdateUserRequest;
    }): Promise<UpdateUserResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_UpdateUser(id, request);
    },
    // Profile fields (display name, email, can-pack) don't affect effective
    // permissions, so only the users list needs refreshing.
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};

export const useEntraAccessUsers = () => {
  return useQuery({
    queryKey: keys.entraUsers,
    queryFn: async (): Promise<GetEntraAccessUsersResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_GetEntraUsers();
    },
    staleTime: 5 * 60 * 1000,
  });
};

export const useAddGroupMember = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      groupId,
      request,
    }: {
      groupId: string;
      request: AddGroupMemberRequest;
    }): Promise<AddGroupMemberResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_AddGroupMember(groupId, request);
    },
    onSuccess: (_data, { groupId }) => {
      queryClient.invalidateQueries({ queryKey: keys.users });
      queryClient.invalidateQueries({ queryKey: keys.group(groupId) });
    },
  });
};

export const useSetUserCanPack = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, canPack }: { id: string; canPack: boolean }): Promise<SetUserCanPackResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_SetCanPack(id, new SetUserCanPackRequest({ userId: id, canPack }));
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};

export const useCreateLocalUser = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (displayName: string): Promise<CreateLocalUserResponse> => {
      const client = getAuthenticatedApiClient();
      return client.authorization_CreateLocalUser(new CreateLocalUserRequest({ displayName }));
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: keys.users });
    },
  });
};

export type { CreateGroupRequest, UpdateGroupRequest, AssignUserGroupsRequest, SetUserActiveRequest, AddGroupMemberRequest, CreateLocalUserRequest, SetUserCanPackRequest, UpdateUserRequest };
