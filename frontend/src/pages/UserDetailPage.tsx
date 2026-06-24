import React, { useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import {
  useUsers,
  useAssignUserGroups,
  useSetUserActive,
  useUserPermissions,
  useUpdateUser,
} from "../api/hooks/useAccessManagement";
import { AssignUserGroupsRequest, SetUserActiveRequest, UpdateUserRequest } from "../api/generated/api-client";
import { useToast } from "../contexts/ToastContext";
import GroupsPicker from "../components/access-management/GroupsPicker";
import UnsavedChangesDialog from "../components/dialogs/UnsavedChangesDialog";
import {
  draftsEqual,
  useUnsavedChangesDialog,
} from "../hooks/useUnsavedChangesDialog";
import ErrorState from "../components/common/ErrorState";

interface UserDraft {
  displayName: string;
  email: string;
  canPack: boolean;
  groupIds: string[];
}

export default function UserDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const toast = useToast();

  const usersQuery = useUsers();
  const permissionsQuery = useUserPermissions(id || null);
  const assignUserGroups = useAssignUserGroups();
  const setActive = useSetUserActive();
  const updateUser = useUpdateUser();

  const [draft, setDraft] = useState<UserDraft | null>(null);
  const [original, setOriginal] = useState<UserDraft | null>(null);
  const initialized = useRef(false);

  const user = usersQuery.data?.users?.find((u) => u.id === id);

  useEffect(() => {
    if (initialized.current) return;
    if (!user) return;

    const d: UserDraft = {
      displayName: user.displayName ?? "",
      email: user.email ?? "",
      canPack: user.canPack ?? false,
      groupIds: user.groupIds ?? [],
    };
    setDraft(d);
    setOriginal(d);
    initialized.current = true;
  }, [user]);

  const updateDraft = (patch: Partial<UserDraft>) =>
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));

  const onSave = async (): Promise<boolean> => {
    if (!draft) return false;
    if (!draft.displayName.trim()) {
      toast.showError("Save failed", "Display name is required");
      return false;
    }
    const [profileResult, groupResult] = await Promise.allSettled([
      updateUser.mutateAsync({
        id,
        request: new UpdateUserRequest({
          userId: id,
          displayName: draft.displayName.trim(),
          email: draft.email.trim(),
          canPack: draft.canPack,
        }),
      }),
      assignUserGroups.mutateAsync({
        id,
        request: new AssignUserGroupsRequest({ userId: id, groupIds: draft.groupIds }),
      }),
    ]);

    const profileFailed = profileResult.status === "rejected";
    const groupFailed = groupResult.status === "rejected";
    if (profileFailed || groupFailed) {
      const part =
        profileFailed && groupFailed ? "changes" : profileFailed ? "profile" : "group assignment";
      toast.showError("Save failed", `Could not save ${part}. Please try again.`);
      return false;
    }
    toast.showSuccess("Saved", "User updated successfully");
    setOriginal(draft);
    return true;
  };

  const isDirty = !draftsEqual(draft, original);
  const { dialogProps, requestNavigation } = useUnsavedChangesDialog(
    isDirty,
    onSave,
  );

  const onToggleActive = async () => {
    if (!user?.id) return;
    try {
      await setActive.mutateAsync({
        id: user.id,
        request: new SetUserActiveRequest({ userId: user.id, isActive: !user.isActive }),
      });
      toast.showSuccess(
        user.isActive ? "User disabled" : "User enabled",
        user.isActive ? "The user has been disabled" : "The user has been enabled"
      );
    } catch {
      toast.showError("Action failed", "Could not update user status");
    }
  };

  const isLoading = usersQuery.isLoading;
  const isSaving = assignUserGroups.isPending || updateUser.isPending;

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading user…</div>
      </div>
    );
  }

  if (usersQuery.isError) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <ErrorState message="Failed to load users." className="flex-1" />
      </div>
    );
  }

  if (!user) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <p className="text-gray-500">User not found.</p>
        <button
          type="button"
          onClick={() => requestNavigation("/admin/access/users")}
          className="text-gray-500 hover:text-gray-700 text-sm mt-2"
        >
          ← Access management
        </button>
      </div>
    );
  }

  if (!draft) return null;

  const lastLoginText = user.lastLoginAt
    ? user.lastLoginAt.toLocaleDateString("cs-CZ", {
        day: "numeric",
        month: "long",
        year: "numeric",
      })
    : "Never logged in";

  return (
    <div className="p-8 max-w-5xl mx-auto space-y-8">
      <div className="flex items-center gap-4">
        <button
          type="button"
          onClick={() => requestNavigation("/admin/access/users")}
          className="text-gray-500 hover:text-gray-700 text-sm"
        >
          ← Access management
        </button>
        <h1 className="text-2xl font-semibold text-gray-900">{user.displayName}</h1>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4 space-y-4">
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-500">Last login: {lastLoginText}</p>
          <button
            type="button"
            onClick={onToggleActive}
            disabled={setActive.isPending}
            className={`text-sm ${user.isActive ? "text-red-600" : "text-green-600"} hover:underline disabled:opacity-50`}
            aria-label={user.isActive ? "Disable user" : "Enable user"}
          >
            {user.isActive ? "Disable" : "Enable"}
          </button>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <label className="block">
            <span className="block text-sm font-medium text-gray-700 mb-1">Display name</span>
            <input
              type="text"
              value={draft.displayName}
              onChange={(e) => updateDraft({ displayName: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </label>
          <label className="block">
            <span className="block text-sm font-medium text-gray-700 mb-1">Email</span>
            <input
              type="email"
              value={draft.email}
              onChange={(e) => updateDraft({ email: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500"
            />
          </label>
        </div>

        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={draft.canPack}
            onChange={(e) => updateDraft({ canPack: e.target.checked })}
            className="rounded border-gray-300 accent-indigo-600"
          />
          <span className="text-sm text-gray-700">Can pack</span>
        </label>
      </div>

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Group membership</h2>
        <GroupsPicker
          value={draft.groupIds}
          onChange={(groupIds) => updateDraft({ groupIds })}
        />
      </section>

      <section>
        <h2 className="text-lg font-medium text-gray-900 mb-3">Effective permissions</h2>
        <p className="text-sm text-gray-500 mb-3">
          Reflects the last saved group assignment. Updates after Save.
        </p>
        {permissionsQuery.isLoading ? (
          <div className="text-gray-500 text-sm">Loading permissions…</div>
        ) : (
          <div className="flex flex-wrap gap-2">
            {(permissionsQuery.data?.permissions ?? []).map((p) => (
              <span
                key={p}
                className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-indigo-100 text-indigo-800"
              >
                {p}
              </span>
            ))}
            {(permissionsQuery.data?.permissions ?? []).length === 0 && (
              <p className="text-sm text-gray-500">No permissions assigned.</p>
            )}
          </div>
        )}
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
          onClick={() => requestNavigation("/admin/access/users")}
          disabled={isSaving}
          className="px-5 py-2 border border-gray-300 text-gray-700 rounded-md text-sm font-medium hover:bg-gray-50 disabled:opacity-50"
        >
          Cancel
        </button>
      </div>

      <UnsavedChangesDialog {...dialogProps} />
    </div>
  );
}
