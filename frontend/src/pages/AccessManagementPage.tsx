import React, { useState } from "react";
import { PAGE_CONTAINER_HEIGHT } from "../constants/layout";
import { useScreenView } from "../telemetry/useScreenView";
import GroupsGrid from "../components/pages/access/GroupsGrid";
import UsersGrid from "../components/pages/access/UsersGrid";

export default function AccessManagementPage() {
  const [tab, setTab] = useState<"users" | "groups">("users");

  useScreenView("Admin", "AccessManagement");

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900">Access management</h1>
      </div>

      <div className="flex-shrink-0 border-b border-gray-200 mb-4">
        <nav className="flex gap-6" aria-label="Tabs">
          <button
            onClick={() => setTab("users")}
            className={`py-2 text-sm font-medium border-b-2 transition-colors ${
              tab === "users"
                ? "border-indigo-500 text-indigo-600"
                : "border-transparent text-gray-500 hover:text-gray-700"
            }`}
          >
            Users
          </button>
          <button
            onClick={() => setTab("groups")}
            className={`py-2 text-sm font-medium border-b-2 transition-colors ${
              tab === "groups"
                ? "border-indigo-500 text-indigo-600"
                : "border-transparent text-gray-500 hover:text-gray-700"
            }`}
          >
            Groups
          </button>
        </nav>
      </div>

      {tab === "users" ? <UsersGrid /> : <GroupsGrid />}
    </div>
  );
}
