import React, { useState } from "react";
import {
  useGroups,
  useUsers,
  useCatalogue,
  useDeleteGroup,
  useSetUserActive,
} from "../api/hooks/useAccessManagement";
import { SetUserActiveRequest } from "../api/generated/api-client";

const AccessManagementPage: React.FC = () => {
  const [tab, setTab] = useState<"groups" | "users">("groups");
  const groups = useGroups();
  const users = useUsers();
  const catalogue = useCatalogue();
  const deleteGroup = useDeleteGroup();
  const setActive = useSetUserActive();

  return (
    <div className="p-8 max-w-5xl mx-auto">
      <h1 className="text-2xl font-semibold text-gray-900 mb-4">Access management</h1>

      <div className="flex gap-2 mb-6">
        <button
          onClick={() => setTab("groups")}
          className={`px-4 py-2 rounded ${tab === "groups" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >Groups</button>
        <button
          onClick={() => setTab("users")}
          className={`px-4 py-2 rounded ${tab === "users" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >Users</button>
      </div>

      {tab === "groups" && (
        <div className="space-y-3">
          {groups.isLoading && <div className="text-gray-500">Loading groups…</div>}
          {groups.data?.groups?.map((g) => (
            <div key={g.id} className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4">
              <div>
                <div className="flex items-center gap-2">
                  <span className="font-medium text-gray-900">{g.name}</span>
                  {g.isSystem && <span className="text-xs bg-gray-100 text-gray-700 px-2 py-0.5 rounded">system</span>}
                </div>
                <p className="text-sm text-gray-500">{g.permissionCount} permissions · {g.memberCount} members</p>
              </div>
              {!g.isSystem && (
                <button
                  onClick={() => g.id && deleteGroup.mutate(g.id)}
                  disabled={deleteGroup.isPending}
                  className="text-sm text-red-600 hover:underline"
                  aria-label={`Delete ${g.name}`}
                >Delete</button>
              )}
            </div>
          ))}
          <p className="text-xs text-gray-400">
            {catalogue.data?.permissions?.length ?? 0} permissions available.
          </p>
        </div>
      )}

      {tab === "users" && (
        <div className="space-y-3">
          {users.isLoading && <div className="text-gray-500">Loading users…</div>}
          {users.data?.users?.map((u) => (
            <div key={u.id} className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4">
              <div>
                <div className="font-medium text-gray-900">{u.displayName}</div>
                <p className="text-sm text-gray-500">{u.email} · {u.groupIds?.length ?? 0} groups</p>
              </div>
              <button
                onClick={() =>
                  u.id &&
                  setActive.mutate({
                    id: u.id,
                    request: new SetUserActiveRequest({ userId: u.id, isActive: !u.isActive }),
                  })
                }
                disabled={setActive.isPending}
                className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                aria-label={`Toggle active ${u.email}`}
              >{u.isActive ? "Disable" : "Enable"}</button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default AccessManagementPage;
