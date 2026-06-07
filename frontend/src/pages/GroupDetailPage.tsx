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
  allUsers: Array<{ id?: string | null; groupIds?: string[] | null }>
): Array<{ id: string; request: { userId: string; groupIds: string[] } }> {
  const originalIds = new Set(original?.memberUserIds ?? []);
  const newIds = new Set(draft.memberUserIds);
  const result: Array<{ id: string; request: { userId: string; groupIds: string[] } }> = [];

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
          request: { userId, groupIds: (user.groupIds ?? []).filter((g) => g !== groupId) },
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

  const onSave = async () => {
    if (!draft) return;
    if (!draft.name.trim()) {
      toast.showError("Validation error", "Group name is required");
      return;
    }

    try {
      if (isCreateMode) {
        const result = await createGroup.mutateAsync(
          new CreateGroupRequest({
            name: draft.name.trim(),
            description: draft.description,
            permissions: draft.permissions,
            parentGroupIds: draft.parentGroupIds,
          })
        );
        toast.showSuccess("Group created", "The new group has been saved");
        navigate(`/admin/access/groups/${result.id}`);
        return;
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

      const memberArgs = buildMemberMutationArgs(draft, original, id, usersQuery.data?.users ?? []);
      await Promise.all(
        memberArgs.map(({ id: userId, request }) =>
          assignUserGroups.mutateAsync({
            id: userId,
            request: new AssignUserGroupsRequest(request),
          })
        )
      );

      toast.showSuccess("Saved", "Group updated successfully");
      setOriginal(draft);
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      if (msg.includes("AuthorizationGroupCycleDetected")) {
        toast.showError("Cycle detected", "This would create a circular group dependency");
      } else {
        toast.showError("Save failed", "An error occurred while saving changes");
      }
    }
  };

  const onCancel = () => navigate("/admin/access");

  const isSaving = updateGroup.isPending || createGroup.isPending || assignUserGroups.isPending;
  const isLoading = !isCreateMode && (groupQuery.isLoading || usersQuery.isLoading);

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading group…</div>
      </div>
    );
  }

  if (!draft) return null;

  return (
    <div className="p-8 max-w-5xl mx-auto space-y-8">
      <div className="flex items-center gap-4">
        <button
          type="button"
          onClick={onCancel}
          className="text-gray-500 hover:text-gray-700 text-sm"
        >
          ← Access management
        </button>
        <h1 className="text-2xl font-semibold text-gray-900">
          {isCreateMode ? "New group" : "Edit group"}
        </h1>
      </div>

      <div className="space-y-4">
        <div>
          <label htmlFor="group-name" className="block text-sm font-medium text-gray-700 mb-1">
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
          <label htmlFor="group-desc" className="block text-sm font-medium text-gray-700 mb-1">
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

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Permissions</h2>
        <PermissionPicker
          value={draft.permissions}
          onChange={(permissions) => updateDraft({ permissions })}
        />
      </section>

      {!isCreateMode && (
        <section>
          <h2 className="text-lg font-medium text-gray-900 mb-3">Included groups</h2>
          <p className="text-sm text-gray-500 mb-3">
            Groups in the right column are building blocks of this group — their permissions are
            inherited.
          </p>
          <IncludedGroupsPicker
            currentGroupId={id}
            value={draft.parentGroupIds}
            onChange={(parentGroupIds) => updateDraft({ parentGroupIds })}
          />
        </section>
      )}

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Members</h2>
        {!isCreateMode && (
          <EntraMemberSearch
            groupId={id}
            currentMemberIds={draft.memberUserIds}
            onMemberAdded={(userId) =>
              updateDraft({ memberUserIds: [...draft.memberUserIds, userId] })
            }
          />
        )}
        <MembersPicker
          value={draft.memberUserIds}
          onChange={(memberUserIds) => updateDraft({ memberUserIds })}
        />
      </section>

      <div className="flex gap-3 pt-4 border-t border-gray-200">
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
  );
}
