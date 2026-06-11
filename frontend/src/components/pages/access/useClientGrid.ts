import { useEffect, useMemo, useState } from "react";

type SortValue = string | number | undefined | null;

export interface ClientGrid<T> {
  sortBy: string;
  sortDescending: boolean;
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  pageItems: T[];
  handleSort: (column: string) => void;
  handlePageChange: (page: number) => void;
  handlePageSizeChange: (size: number) => void;
}

interface ClientGridOptions {
  defaultSortBy?: string;
  defaultPageSize?: number;
}

const DEFAULT_PAGE_SIZE = 20;

// Client-side sort + paging for the small access-management lists.
// `getSortValue` must be a stable reference (declare it at module scope) so the
// memoized sort does not recompute on every render.
export function useClientGrid<T>(
  items: T[],
  getSortValue: (item: T, column: string) => SortValue,
  options: ClientGridOptions = {},
): ClientGrid<T> {
  const [sortBy, setSortBy] = useState(options.defaultSortBy ?? "");
  const [sortDescending, setSortDescending] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(options.defaultPageSize ?? DEFAULT_PAGE_SIZE);

  useEffect(() => {
    setPageNumber(1);
  }, [items]);

  const sorted = useMemo(() => {
    if (!sortBy) return items;
    return [...items].sort((a, b) => {
      const result = compareValues(getSortValue(a, sortBy), getSortValue(b, sortBy));
      return sortDescending ? -result : result;
    });
  }, [items, sortBy, sortDescending, getSortValue]);

  const totalCount = sorted.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const safePage = Math.min(pageNumber, totalPages);

  const pageItems = useMemo(
    () => sorted.slice((safePage - 1) * pageSize, safePage * pageSize),
    [sorted, safePage, pageSize],
  );

  const handleSort = (column: string) => {
    if (sortBy === column) {
      setSortDescending((descending) => !descending);
    } else {
      setSortBy(column);
      setSortDescending(false);
    }
    setPageNumber(1);
  };

  const handlePageChange = (page: number) => {
    if (page >= 1 && page <= totalPages) {
      setPageNumber(page);
    }
  };

  const handlePageSizeChange = (size: number) => {
    setPageSize(size);
    setPageNumber(1);
  };

  return {
    sortBy,
    sortDescending,
    pageNumber: safePage,
    pageSize,
    totalCount,
    totalPages,
    pageItems,
    handleSort,
    handlePageChange,
    handlePageSizeChange,
  };
}

function compareValues(a: SortValue, b: SortValue): number {
  if (a == null && b == null) return 0;
  if (a == null) return -1;
  if (b == null) return 1;
  if (typeof a === "number" && typeof b === "number") return a - b;
  return String(a).localeCompare(String(b), "cs");
}
