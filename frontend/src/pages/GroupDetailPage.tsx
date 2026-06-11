import React, { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  useGroup,
  useGroups,
  useCatalogue,
  useUsers,
  useUpdateGroup,
  useCreateGroup,
  useAssignUserGroups,
} from "../api/hooks/useAccessManagement";
import {
  UpdateGroupRequest,
  CreateGroupRequest,
  AssignUserGroupsRequest,
} from "../api/generated/api-client";
import { useToast } from "../contexts/ToastContext";
import PermissionPicker from "../components/access-management/PermissionPicker";
import IncludedGroupsPicker from "../components/access-management/IncludedGroupsPicker";
import MembersPicker from "../components/access-management/MembersPicker";
import EntraMemberSearch from "../components/access-management/EntraMemberSearch";
import UnsavedChangesDialog from "../components/dialogs/UnsavedChangesDialog";
import {
  draftsEqual,
  useUnsavedChangesDialog,
} from "../hooks/useUnsavedChangesDialog";

interface GroupDraft {
  name: string;
  description: string;
  permissions: string[];
  parentGroupIds: string[];
  memberUserIds: string[];
}

const EMPTY_DRAFT: GroupDraft = {
  name: "",
  description: "",
  permissions: [],
  parentGroupIds: [],
  memberUserIds: [],
};

function buildMemberMutationArgs(
  draft: { memberUserIds: string[] },
  original: { memberUserIds: string[] } | null,
  groupId: string,
  allUsers: Array<{ id?: string | null; groupIds?: string[] | null }>,
): Array<{ id: string; request: { userId: string; groupIds: string[] } }> {
  const originalIds = new Set(original?.memberUserIds ?? []);
  const newIds = new Set(draft.memberUserIds);
  const result: Array<{
    id: string;
    request: { userId: string; groupIds: string[] };
  }> = [];

  for (const userId of draft.memberUserIds) {
    if (!originalIds.has(userId)) {
      const user = allUsers.find((u) => u.id === userId);
      if (user?.id) {
        result.push({
          id: userId,
          request: { userId, groupIds: [...(user.groupIds ?? []), groupId] },
        });
      }
    }
  }

  for (const userId of original?.memberUserIds ?? []) {
    if (!newIds.has(userId)) {
      const user = allUsers.find((u) => u.id === userId);
      if (user?.id) {
        result.push({
          id: userId,
          request: {
            userId,
            groupIds: (user.groupIds ?? []).filter((g) => g !== groupId),
          },
        });
      }
    }
  }

  return result;
}

export default function GroupDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();

  const isCreateMode = id === "new";

  const groupQuery = useGroup(isCreateMode ? null : id);
  const usersQuery = useUsers();
  useCatalogue();
  useGroups();

  const updateGroup = useUpdateGroup();
  const createGroup = useCreateGroup();
  const assignUserGroups = useAssignUserGroups();

  const [draft, setDraft] = useState<GroupDraft | null>(null);
  const [original, setOriginal] = useState<GroupDraft | null>(null);
  const initialized = useRef(false);

  useEffect(() => {
    if (initialized.current) return;

    if (isCreateMode) {
      setDraft(EMPTY_DRAFT);
      setOriginal(EMPTY_DRAFT);
      initialized.current = true;
      return;
    }

    const group = groupQuery.data?.group;
    const users = usersQuery.data?.users;
    if (!group || !users) return;

    const memberUserIds = users
      .filter((u) => u.groupIds?.includes(group.id ?? ""))
      .map((u) => u.id)
      .filter((uid): uid is string => Boolean(uid));

    const d: GroupDraft = {
      name: group.name ?? "",
      description: group.description ?? "",
      permissions: group.permissions ?? [],
      parentGroupIds: group.parentGroupIds ?? [],
      memberUserIds,
    };
    setDraft(d);
    setOriginal(d);
    initialized.current = true;
  }, [groupQuery.data, usersQuery.data, isCreateMode]);

  const updateDraft = (patch: Partial<GroupDraft>) =>
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));

  const onSave = async (): Promise<boolean> => {
    if (!draft) return false;
    if (!draft.name.trim()) {
      toast.showError("Validation error", "Group name is required");
      return false;
    }

    try {
      if (isCreateMode) {
        const result = await createGroup.mutateAsync(
          new CreateGroupRequest({
            name: draft.name.trim(),
            description: draft.description,
            permissions: draft.permissions,
            parentGroupIds: draft.parentGroupIds,
          }),
        );
        toast.showSuccess("Group created", "The new group has been saved");
        setOriginal(draft);
        navigate(`/admin/access/groups/${result.id}`);
        return true;
      }

      await updateGroup.mutateAsync({
        id,
        request: new UpdateGroupRequest({
          id,
          name: draft.name.trim(),
          description: draft.description,
          permissions: draft.permissions,
          parentGroupIds: draft.parentGroupIds,
        }),
      });

      const { data: freshUsersData } = await usersQuery.refetch();
      const memberArgs = buildMemberMutationArgs(
        draft,
        original,
        id,
        freshUsersData?.users ?? [],
      );
      await Promise.all(
        memberArgs.map(({ id: userId, request }) =>
          assignUserGroups.mutateAsync({
            id: userId,
            request: new AssignUserGroupsRequest(request),
          }),
        ),
      );

      toast.showSuccess("Saved", "Group updated successfully");
      setOriginal(draft);
      return true;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      if (msg.includes("AuthorizationGroupCycleDetected")) {
        toast.showError(
          "Cycle detected",
          "This would create a circular group dependency",
        );
      } else {
        toast.showError(
          "Save failed",
          "An error occurred while saving changes",
        );
      }
      return false;
    }
  };

  const isDirty = !draftsEqual(draft, original);
  const { dialogProps, requestNavigation } = useUnsavedChangesDialog(
    isDirty,
    onSave,
  );

  const onCancel = () => requestNavigation("/admin/access/groups");

  const isSaving =
    updateGroup.isPending ||
    createGroup.isPending ||
    assignUserGroups.isPending;
  const isLoading =
    !isCreateMode && (groupQuery.isLoading || usersQuery.isLoading);

  if (isLoading) {
    return (
      <div className="flex flex-col h-full w-full p-3 md:p-4">
        <div className="text-gray-500">Loading group…</div>
      </div>
    );
  }

  if (!draft) return null;

  return (
    <div className="flex flex-col h-full w-full p-3 md:p-4">
      <div className="flex-shrink-0 flex items-center justify-between gap-4 mb-3">
        <div className="flex items-center gap-4 min-w-0">
          <button
            type="button"
            onClick={onCancel}
            className="text-gray-500 hover:text-gray-700 text-sm flex-shrink-0"
          >
            ← Access management
          </button>
          <h1 className="text-2xl font-semibold text-gray-900 truncate">
            {isCreateMode ? "New group" : "Edit group"}
          </h1>
        </div>
        <div className="flex gap-3 flex-shrink-0">
          <button
            type="button"
            onClick={onSave}
            disabled={isSaving}
            className="px-5 py-2 bg-indigo-600 text-white rounded-md text-sm font-medium hover:bg-indigo-700 disabled:opacity-50"
          >
            {isSaving ? "Saving…" : "Save"}
          </button>
          <button
            type="button"
            onClick={onCancel}
            disabled={isSaving}
            className="px-5 py-2 border border-gray-300 text-gray-700 rounded-md text-sm font-medium hover:bg-gray-50 disabled:opacity-50"
          >
            Cancel
          </button>
        </div>
      </div>

      <div className="flex-shrink-0 bg-white shadow rounded-lg p-4 mb-4">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label
              htmlFor="group-name"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Name
            </label>
            <input
              id="group-name"
              type="text"
              value={draft.name}
              onChange={(e) => updateDraft({ name: e.target.value })}
              aria-label="Name"
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
          <div>
            <label
              htmlFor="group-desc"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Description
            </label>
            <input
              id="group-desc"
              type="text"
              value={draft.description}
              onChange={(e) => updateDraft({ description: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </div>
        </div>
      </div>

      <div
        className={`flex-1 min-h-0 grid gap-4 ${
          isCreateMode
            ? "grid-cols-1 grid-rows-1"
            : "grid-cols-1 xl:grid-cols-3 xl:grid-rows-1"
        }`}
      >
        <section className="bg-white shadow rounded-lg p-4 flex flex-col min-h-0">
          <h2 className="text-lg font-medium text-gray-900 mb-3 flex-shrink-0">
            Permissions
          </h2>
          <div className="flex-1 min-h-0">
            <PermissionPicker
              value={draft.permissions}
              onChange={(permissions) => updateDraft({ permissions })}
              fillHeight
            />
          </div>
        </section>

        {!isCreateMode && (
          <section className="bg-white shadow rounded-lg p-4 flex flex-col min-h-0">
            <div className="flex-shrink-0">
              <h2 className="text-lg font-medium text-gray-900 mb-1">
                Included groups
              </h2>
              <p className="text-sm text-gray-500 mb-3">
                Groups in the right column are building blocks of this group —
                their permissions are inherited.
              </p>
            </div>
            <div className="flex-1 min-h-0">
              <IncludedGroupsPicker
                currentGroupId={id}
                value={draft.parentGroupIds}
                onChange={(parentGroupIds) => updateDraft({ parentGroupIds })}
                fillHeight
              />
            </div>
          </section>
        )}

        {!isCreateMode && (
          <section className="bg-white shadow rounded-lg p-4 flex flex-col min-h-0">
            <h2 className="text-lg font-medium text-gray-900 mb-3 flex-shrink-0">
              Members
            </h2>
            <EntraMemberSearch
              groupId={id}
              currentMemberIds={draft.memberUserIds}
              onMemberAdded={(userId) =>
                updateDraft({ memberUserIds: [...draft.memberUserIds, userId] })
              }
            />
            <div className="flex-1 min-h-0">
              <MembersPicker
                value={draft.memberUserIds}
                onChange={(memberUserIds) => updateDraft({ memberUserIds })}
                fillHeight
              />
            </div>
          </section>
        )}
      </div>

      <UnsavedChangesDialog {...dialogProps} />
    </div>
  );
}
