import React, { useEffect, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  useUsers,
  useAssignUserGroups,
  useSetUserActive,
  useUserPermissions,
} from "../api/hooks/useAccessManagement";
import { AssignUserGroupsRequest, SetUserActiveRequest } from "../api/generated/api-client";
import { useToast } from "../contexts/ToastContext";
import GroupsPicker from "../components/access-management/GroupsPicker";

interface UserDraft {
  groupIds: string[];
}

export default function UserDetailPage() {
  const { id = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const toast = useToast();

  const usersQuery = useUsers();
  const permissionsQuery = useUserPermissions(id || null);
  const assignUserGroups = useAssignUserGroups();
  const setActive = useSetUserActive();

  const [draft, setDraft] = useState<UserDraft | null>(null);
  const initialized = useRef(false);

  const user = usersQuery.data?.users?.find((u) => u.id === id);

  useEffect(() => {
    if (initialized.current) return;
    if (!user) return;

    const d: UserDraft = { groupIds: user.groupIds ?? [] };
    setDraft(d);
    initialized.current = true;
  }, [user]);

  const updateDraft = (patch: Partial<UserDraft>) =>
    setDraft((prev) => (prev ? { ...prev, ...patch } : prev));

  const onSave = async () => {
    if (!draft) return;
    try {
      await assignUserGroups.mutateAsync({
        id,
        request: new AssignUserGroupsRequest({ userId: id, groupIds: draft.groupIds }),
      });
      toast.showSuccess("Saved", "User groups updated successfully");
    } catch {
      toast.showError("Save failed", "An error occurred while saving changes");
    }
  };

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
  const isSaving = assignUserGroups.isPending;

  if (isLoading) {
    return (
      <div className="p-8 max-w-5xl mx-auto">
        <div className="text-gray-500">Loading user…</div>
      </div>
    );
  }

  if (!draft || !user) return null;

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
          onClick={() => navigate("/admin/access")}
          className="text-gray-500 hover:text-gray-700 text-sm"
        >
          ← Access management
        </button>
        <h1 className="text-2xl font-semibold text-gray-900">{user.displayName}</h1>
      </div>

      <div className="bg-white border border-gray-200 rounded-lg p-4 flex items-center justify-between">
        <div className="space-y-1">
          <p className="text-sm text-gray-700">{user.email}</p>
          <p className="text-sm text-gray-500">Last login: {lastLoginText}</p>
        </div>
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
          onClick={() => navigate("/admin/access")}
          disabled={isSaving}
          className="px-5 py-2 border border-gray-300 text-gray-700 rounded-md text-sm font-medium hover:bg-gray-50 disabled:opacity-50"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}
