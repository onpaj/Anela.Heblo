import React, { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Edit, Filter, Search } from "lucide-react";
import {
  useUsers,
  useSetUserActive,
  useSetUserCanPack,
  useCreateLocalUser,
} from "../../../api/hooks/useAccessManagement";
import { AppUserDto, SetUserActiveRequest } from "../../../api/generated/api-client";
import Pagination from "../../common/Pagination";
import LoadingState from "../../common/LoadingState";
import ErrorState from "../../common/ErrorState";
import SortableHeader from "./SortableHeader";
import { useClientGrid } from "./useClientGrid";
import { useIsMobile } from "../../../hooks/useMediaQuery";

type SourceFilter = "" | "Local" | "Entra";

const SOURCE_OPTIONS: { value: SourceFilter; label: string }[] = [
  { value: "", label: "All" },
  { value: "Entra", label: "Entra" },
  { value: "Local", label: "Local" },
];

// Stable sort-value accessor (declared at module scope for useClientGrid memoization).
const getUserSortValue = (user: AppUserDto, column: string) => {
  switch (column) {
    case "displayName":
      return user.displayName?.toLowerCase();
    case "email":
      return user.email?.toLowerCase();
    case "source":
      return user.source;
    case "groups":
      return user.groupIds?.length ?? 0;
    case "lastLoginAt":
      return user.lastLoginAt ? user.lastLoginAt.getTime() : 0;
    default:
      return undefined;
  }
};

const formatLastLogin = (date?: Date): string =>
  date
    ? date.toLocaleDateString("cs-CZ", {
        day: "numeric",
        month: "numeric",
        year: "numeric",
      })
    : "";

const UsersGrid: React.FC = () => {
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const users = useUsers();
  const setActive = useSetUserActive();
  const setCanPack = useSetUserCanPack();
  const createLocalUser = useCreateLocalUser();

  const [search, setSearch] = useState("");
  const [sourceFilter, setSourceFilter] = useState<SourceFilter>("");
  const [showDisabled, setShowDisabled] = useState(false);
  const [packersOnly, setPackersOnly] = useState(false);
  const [newLocalName, setNewLocalName] = useState("");

  const allUsers = useMemo(() => users.data?.users ?? [], [users.data]);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    return allUsers.filter((u) => {
      if (!showDisabled && u.isActive === false) return false;
      if (sourceFilter && u.source !== sourceFilter) return false;
      if (packersOnly && !u.canPack) return false;
      if (query) {
        const name = (u.displayName ?? "").toLowerCase();
        const email = (u.email ?? "").toLowerCase();
        if (!name.includes(query) && !email.includes(query)) return false;
      }
      return true;
    });
  }, [allUsers, search, sourceFilter, showDisabled, packersOnly]);

  const grid = useClientGrid(filtered, getUserSortValue, {
    defaultSortBy: "displayName",
  });

  const isFiltered = Boolean(search || sourceFilter || packersOnly || showDisabled);

  const handleCreateLocalUser = (event: React.FormEvent) => {
    event.preventDefault();
    const name = newLocalName.trim();
    if (name) {
      createLocalUser.mutate(name, { onSuccess: () => setNewLocalName("") });
    }
  };

  if (users.isLoading) {
    return <LoadingState message="Loading users…" className="flex-1" />;
  }

  if (users.isError) {
    return <ErrorState message="Failed to load users." className="flex-1" />;
  }

  return (
    <div className="flex-1 flex flex-col min-h-0">
      {/* Filters + create form */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4 space-y-3">
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-3 flex-1 min-w-0 flex-wrap">
            <div className="flex items-center">
              <Filter className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">Filters:</span>
            </div>

            <div className="flex-1 min-w-0 md:max-w-xs">
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                  <Search className="h-4 w-4 text-gray-400 dark:text-graphite-faint" />
                </div>
                <input
                  type="text"
                  aria-label="Search users"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  placeholder="Name or email…"
                />
              </div>
            </div>

            <div className="flex items-center gap-2">
              <span className="text-sm text-gray-700 dark:text-graphite-muted">Source:</span>
              <div className="inline-flex rounded-md shadow-sm" role="group" aria-label="Source">
                {SOURCE_OPTIONS.map((option, index) => {
                  const isActive = sourceFilter === option.value;
                  const isFirst = index === 0;
                  const isLast = index === SOURCE_OPTIONS.length - 1;
                  return (
                    <button
                      key={option.label}
                      type="button"
                      onClick={() => setSourceFilter(option.value)}
                      aria-pressed={isActive}
                      className={`px-3 py-2 text-sm font-medium border border-gray-300 dark:border-graphite-border ${isFirst ? "rounded-l-md" : "-ml-px"} ${isLast ? "rounded-r-md" : ""} ${
                        isActive
                          ? "z-10 bg-indigo-600 text-white border-indigo-600"
                          : "bg-white text-gray-700 hover:bg-gray-50 dark:bg-graphite-surface dark:text-graphite-muted dark:hover:bg-white/5"
                      }`}
                    >
                      {option.label}
                    </button>
                  );
                })}
              </div>
            </div>

            <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-muted">
              <input
                type="checkbox"
                checked={showDisabled}
                onChange={(e) => setShowDisabled(e.target.checked)}
                className="rounded border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 text-indigo-600 focus:ring-indigo-500"
              />
              Show disabled
            </label>

            <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-muted">
              <input
                type="checkbox"
                checked={packersOnly}
                onChange={(e) => setPackersOnly(e.target.checked)}
                className="rounded border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 text-indigo-600 focus:ring-indigo-500"
              />
              Packers only
            </label>
          </div>

          <form className="flex items-center gap-2" onSubmit={handleCreateLocalUser}>
            <input
              value={newLocalName}
              onChange={(e) => setNewLocalName(e.target.value)}
              placeholder="New local operator name"
              className="rounded border border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint px-3 py-2 text-sm"
            />
            <button
              type="submit"
              disabled={createLocalUser.isPending}
              className="rounded bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              Create local operator
            </button>
          </form>
        </div>

        {createLocalUser.isError && (
          <p className="text-sm text-red-600">Failed to create operator. Please try again.</p>
        )}
        {setCanPack.isError && (
          <p className="text-sm text-red-600">Failed to update packing permission. Please try again.</p>
        )}
        {setActive.isError && (
          <p className="text-sm text-red-600">Failed to update user status. Please try again.</p>
        )}
      </div>

      {/* Grid (table on desktop, cards on mobile) */}
      {isMobile ? (
        <div className="flex-1 overflow-auto space-y-2">
          {grid.pageItems.map((u) => (
            <div key={u.id} className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4">
              <div className="flex items-start justify-between gap-2">
                <button
                  onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                  className="font-medium text-gray-900 dark:text-graphite-text text-left truncate"
                >
                  {u.displayName}
                </button>
                <div className="flex items-center gap-1 flex-shrink-0">
                  {u.source === "Local" ? (
                    <span className="rounded bg-amber-100 dark:bg-amber-900/30 px-2 py-0.5 text-xs font-medium text-amber-700 dark:text-amber-300">
                      Local
                    </span>
                  ) : (
                    <span className="rounded bg-gray-100 dark:bg-graphite-surface-2 px-2 py-0.5 text-xs font-medium text-gray-600 dark:text-graphite-muted">
                      Entra
                    </span>
                  )}
                  {u.isActive ? (
                    <span className="rounded bg-green-100 dark:bg-green-900/30 px-2 py-0.5 text-xs font-medium text-green-700 dark:text-green-300">
                      Active
                    </span>
                  ) : (
                    <span className="rounded bg-red-100 dark:bg-red-900/30 px-2 py-0.5 text-xs font-medium text-red-700 dark:text-red-300">
                      Disabled
                    </span>
                  )}
                </div>
              </div>
              <p className="text-sm text-gray-500 dark:text-graphite-muted truncate">{u.email}</p>
              <p className="text-sm text-gray-500 dark:text-graphite-muted mt-1">
                {u.groupIds?.length ?? 0} groups
                {u.canPack && (
                  <span className="ml-2 rounded bg-indigo-100 dark:bg-indigo-900/30 px-2 py-0.5 text-xs font-medium text-indigo-700 dark:text-indigo-300">
                    Packer
                  </span>
                )}
              </p>
              <div className="flex items-center justify-between mt-2">
                <span className="text-xs text-gray-500 dark:text-graphite-muted">
                  Last login: {formatLastLogin(u.lastLoginAt) || "—"}
                </span>
                <div className="flex items-center gap-3">
                  <button
                    onClick={() =>
                      u.id && navigate(`/admin/access/users/${u.id}`)
                    }
                    className="p-2 text-gray-400 dark:text-graphite-faint hover:text-indigo-600 dark:hover:text-graphite-accent rounded-lg hover:bg-gray-50 dark:hover:bg-white/5"
                    aria-label={`Edit ${u.displayName}`}
                  >
                    <Edit className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() =>
                      u.id && setCanPack.mutate({ id: u.id, canPack: !u.canPack })
                    }
                    disabled={setCanPack.isPending && setCanPack.variables?.id === u.id}
                    className={`text-sm ${u.canPack ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-500 dark:text-graphite-muted"} hover:underline`}
                    aria-label={`Toggle can pack ${u.displayName}`}
                  >
                    {u.canPack ? "Packer ✓" : "Make packer"}
                  </button>
                  <button
                    onClick={() =>
                      u.id &&
                      setActive.mutate({
                        id: u.id,
                        request: new SetUserActiveRequest({
                          userId: u.id,
                          isActive: !u.isActive,
                        }),
                      })
                    }
                    disabled={setActive.isPending && setActive.variables?.id === u.id}
                    className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                    aria-label={`Toggle active ${u.displayName}`}
                  >
                    {u.isActive ? "Disable" : "Enable"}
                  </button>
                </div>
              </div>
            </div>
          ))}

          {grid.totalCount === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500 dark:text-graphite-muted">No users found.</p>
            </div>
          )}
        </div>
      ) : (
        <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
              <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
                <tr>
                  <SortableHeader column="displayName" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Name
                  </SortableHeader>
                  <SortableHeader column="email" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Email
                  </SortableHeader>
                  <SortableHeader column="source" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Source
                  </SortableHeader>
                  <SortableHeader column="groups" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Groups
                  </SortableHeader>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                    Packer
                  </th>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                    Status
                  </th>
                  <SortableHeader column="lastLoginAt" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Last login
                  </SortableHeader>
                  <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
                {grid.pageItems.map((u) => (
                  <tr key={u.id} className="hover:bg-gray-50 dark:hover:bg-white/5">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                      <button
                        onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                        className="text-gray-900 dark:text-graphite-text hover:text-indigo-600 dark:hover:text-graphite-accent text-left"
                      >
                        {u.displayName}
                      </button>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">{u.email}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {u.source === "Local" ? (
                        <span className="rounded bg-amber-100 dark:bg-amber-900/30 px-2 py-0.5 text-xs font-medium text-amber-700 dark:text-amber-300">Local</span>
                      ) : (
                        <span className="rounded bg-gray-100 dark:bg-graphite-surface-2 px-2 py-0.5 text-xs font-medium text-gray-600 dark:text-graphite-muted">Entra</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">{u.groupIds?.length ?? 0}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {u.canPack ? (
                        <span className="rounded bg-indigo-100 dark:bg-indigo-900/30 px-2 py-0.5 text-xs font-medium text-indigo-700 dark:text-indigo-300">Packer</span>
                      ) : (
                        <span className="text-gray-400 dark:text-graphite-faint">—</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {u.isActive ? (
                        <span className="rounded bg-green-100 dark:bg-green-900/30 px-2 py-0.5 text-xs font-medium text-green-700 dark:text-green-300">Active</span>
                      ) : (
                        <span className="rounded bg-red-100 dark:bg-red-900/30 px-2 py-0.5 text-xs font-medium text-red-700 dark:text-red-300">Disabled</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">{formatLastLogin(u.lastLoginAt)}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => u.id && setCanPack.mutate({ id: u.id, canPack: !u.canPack })}
                          disabled={setCanPack.isPending && setCanPack.variables?.id === u.id}
                          className={`text-sm ${u.canPack ? "text-indigo-600 dark:text-graphite-accent" : "text-gray-500 dark:text-graphite-muted"} hover:underline`}
                          aria-label={`Toggle can pack ${u.displayName}`}
                        >
                          {u.canPack ? "Packer ✓" : "Make packer"}
                        </button>
                        <button
                          onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                          className="p-2 text-gray-400 dark:text-graphite-faint hover:text-indigo-600 dark:hover:text-graphite-accent rounded-lg hover:bg-gray-50 dark:hover:bg-white/5"
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
                          disabled={setActive.isPending && setActive.variables?.id === u.id}
                          className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                          aria-label={`Toggle active ${u.displayName}`}
                        >
                          {u.isActive ? "Disable" : "Enable"}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {grid.totalCount === 0 && (
              <div className="text-center py-8">
                <p className="text-gray-500 dark:text-graphite-muted">No users found.</p>
              </div>
            )}
          </div>
        </div>
      )}

      <Pagination
        totalCount={grid.totalCount}
        pageNumber={grid.pageNumber}
        pageSize={grid.pageSize}
        totalPages={grid.totalPages}
        onPageChange={grid.handlePageChange}
        onPageSizeChange={grid.handlePageSizeChange}
        isFiltered={isFiltered}
      />
    </div>
  );
};

export default UsersGrid;
