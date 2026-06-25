import React, { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Edit, Filter, Search } from "lucide-react";
import {
  useGroups,
  useCatalogue,
  useDeleteGroup,
} from "../../../api/hooks/useAccessManagement";
import { GroupSummaryDto } from "../../../api/generated/api-client";
import Pagination from "../../common/Pagination";
import LoadingState from "../../common/LoadingState";
import ErrorState from "../../common/ErrorState";
import SortableHeader from "./SortableHeader";
import { useClientGrid } from "./useClientGrid";
import { useIsMobile } from "../../../hooks/useMediaQuery";

// Stable sort-value accessor (declared at module scope for useClientGrid memoization).
const getGroupSortValue = (group: GroupSummaryDto, column: string) => {
  switch (column) {
    case "name":
      return group.name?.toLowerCase();
    case "description":
      return group.description?.toLowerCase();
    case "permissions":
      return group.permissionCount ?? 0;
    case "members":
      return group.memberCount ?? 0;
    case "parents":
      return group.parentCount ?? 0;
    default:
      return undefined;
  }
};

const GroupsGrid: React.FC = () => {
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const groups = useGroups();
  const catalogue = useCatalogue();
  const deleteGroup = useDeleteGroup();

  const [search, setSearch] = useState("");

  const allGroups = useMemo(() => groups.data?.groups ?? [], [groups.data]);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return allGroups;
    return allGroups.filter((g) => {
      const name = (g.name ?? "").toLowerCase();
      const description = (g.description ?? "").toLowerCase();
      return name.includes(query) || description.includes(query);
    });
  }, [allGroups, search]);

  const grid = useClientGrid(filtered, getGroupSortValue, { defaultSortBy: "name" });

  if (groups.isLoading) {
    return <LoadingState message="Loading groups…" className="flex-1" />;
  }

  if (groups.isError) {
    return <ErrorState message="Failed to load groups." className="flex-1" />;
  }

  return (
    <div className="flex-1 flex flex-col min-h-0">
      {/* Filters */}
      <div className="flex-shrink-0 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
        <div className="flex items-center justify-between gap-3 flex-wrap">
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
                  aria-label="Search groups"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint rounded-md"
                  placeholder="Name or description…"
                />
              </div>
            </div>
          </div>

          <button
            onClick={() => navigate("/admin/access/groups/new")}
            className="px-4 py-2 bg-indigo-600 text-white rounded text-sm font-medium hover:bg-indigo-700"
            aria-label="New group"
          >
            New group
          </button>
        </div>
      </div>

      {/* Grid (table on desktop, cards on mobile) */}
      {isMobile ? (
        <div className="flex-1 overflow-auto space-y-2">
          {grid.pageItems.map((g) => (
            <div key={g.id} className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4">
              <div className="flex items-start justify-between gap-2">
                <button
                  onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                  className="font-medium text-gray-900 dark:text-graphite-text text-left"
                >
                  {g.name}
                </button>
                <div className="flex items-center gap-2 flex-shrink-0">
                  <button
                    onClick={() =>
                      g.id && navigate(`/admin/access/groups/${g.id}`)
                    }
                    className="p-2 text-gray-400 dark:text-graphite-faint hover:text-indigo-600 dark:hover:text-graphite-accent rounded-lg hover:bg-gray-50 dark:hover:bg-white/5"
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
              {g.description && (
                <p className="text-sm text-gray-500 dark:text-graphite-muted line-clamp-2 mt-1">
                  {g.description}
                </p>
              )}
              <p className="text-xs text-gray-500 dark:text-graphite-muted mt-2">
                {g.permissionCount} permissions · {g.memberCount} members ·{" "}
                {g.parentCount} parents
              </p>
            </div>
          ))}

          {grid.totalCount === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500 dark:text-graphite-muted">No groups found.</p>
            </div>
          )}
        </div>
      ) : (
        <div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border">
              <thead className="bg-gray-50 dark:bg-graphite-surface-2 sticky top-0 z-10">
                <tr>
                  <SortableHeader column="name" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Name
                  </SortableHeader>
                  <SortableHeader column="description" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Description
                  </SortableHeader>
                  <SortableHeader column="permissions" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Permissions
                  </SortableHeader>
                  <SortableHeader column="members" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Members
                  </SortableHeader>
                  <SortableHeader column="parents" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Parents
                  </SortableHeader>
                  <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
                {grid.pageItems.map((g) => (
                  <tr key={g.id} className="hover:bg-gray-50 dark:hover:bg-white/5">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                      <button
                        onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                        className="text-gray-900 dark:text-graphite-text hover:text-indigo-600 dark:hover:text-graphite-accent text-left"
                      >
                        {g.name}
                      </button>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500 dark:text-graphite-muted">{g.description}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">{g.permissionCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">{g.memberCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-graphite-muted">{g.parentCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                          className="p-2 text-gray-400 dark:text-graphite-faint hover:text-indigo-600 dark:hover:text-graphite-accent rounded-lg hover:bg-gray-50 dark:hover:bg-white/5"
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
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {grid.totalCount === 0 && (
              <div className="text-center py-8">
                <p className="text-gray-500 dark:text-graphite-muted">No groups found.</p>
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
        isFiltered={Boolean(search)}
      />

      <p className="flex-shrink-0 mt-2 text-xs text-gray-400 dark:text-graphite-faint">
        {catalogue.data?.permissions?.length ?? 0} permissions available.
      </p>
    </div>
  );
};

export default GroupsGrid;
