import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Edit } from "lucide-react";
import {
  useGroups,
  useUsers,
  useCatalogue,
  useDeleteGroup,
  useSetUserActive,
} from "../api/hooks/useAccessManagement";
import { SetUserActiveRequest } from "../api/generated/api-client";

export default function AccessManagementPage() {
  const [tab, setTab] = useState<"groups" | "users">("groups");
  const navigate = useNavigate();
  const groups = useGroups();
  const users = useUsers();
  const catalogue = useCatalogue();
  const deleteGroup = useDeleteGroup();
  const setActive = useSetUserActive();

  return (
    <div className="p-8 max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-semibold text-gray-900">Access management</h1>
        <button
          onClick={() => navigate("/admin/access/groups/new")}
          className="px-4 py-2 bg-indigo-600 text-white rounded text-sm font-medium hover:bg-indigo-700"
          aria-label="New group"
        >
          New group
        </button>
      </div>

      <div className="flex gap-2 mb-6">
        <button
          onClick={() => setTab("groups")}
          className={`px-4 py-2 rounded ${tab === "groups" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >
          Groups
        </button>
        <button
          onClick={() => setTab("users")}
          className={`px-4 py-2 rounded ${tab === "users" ? "bg-indigo-600 text-white" : "bg-gray-100"}`}
        >
          Users
        </button>
      </div>

      {tab === "groups" && (
        <div className="space-y-3">
          {groups.isLoading && <div className="text-gray-500">Loading groups…</div>}
          {groups.data?.groups?.map((g) => (
            <div
              key={g.id}
              className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4"
            >
              <div className="min-w-0 flex-1">
                <button
                  onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                  className="font-medium text-gray-900 hover:text-indigo-600 text-left"
                >
                  {g.name}
                </button>
                <p className="text-sm text-gray-500">
                  {g.permissionCount} permissions · {g.memberCount} members
                </p>
              </div>
              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                  className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                  aria-label={`Edit ${g.name}`}
                >
                  <Edit className="w-4 h-4" />
                </button>
                <button
                  onClick={() => g.id && deleteGroup.mutate(g.id)}
                  disabled={deleteGroup.isPending}
                  className="text-sm text-red-600 hover:underline"
                  aria-label={`Delete ${g.name}`}
                >
                  Delete
                </button>
              </div>
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
            <div
              key={u.id}
              className="flex items-center justify-between bg-white border border-gray-200 rounded-lg p-4"
            >
              <div className="min-w-0 flex-1">
                <button
                  onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                  className="font-medium text-gray-900 hover:text-indigo-600 text-left"
                >
                  {u.displayName}
                </button>
                <p className="text-sm text-gray-500">
                  {u.email} · {u.groupIds?.length ?? 0} groups
                </p>
              </div>
              <div className="flex items-center gap-2 ml-4">
                <button
                  onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                  className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                  aria-label={`Edit ${u.displayName}`}
                >
                  <Edit className="w-4 h-4" />
                </button>
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
                >
                  {u.isActive ? "Disable" : "Enable"}
                </button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
