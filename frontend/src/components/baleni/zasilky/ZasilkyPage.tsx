import { useCallback, useMemo, useState } from "react";
import { useToast } from "../../../contexts/ToastContext";
import {
  useDeletePackageMutation,
  usePackagesQuery,
  type PackageDto,
} from "../../../api/hooks/usePackages";
import { printLabelPdf } from "../printLabelPdf";
import { ZasilkyFilters, type FilterValues } from "./ZasilkyFilters";
import { ZasilkyTable, type ZasilkySortBy } from "./ZasilkyTable";
import { ZasilkyPagination } from "./ZasilkyPagination";
import { DeletePackageDialog } from "./DeletePackageDialog";
import { useScreenView } from "../../../telemetry/useScreenView";

const PAGE_SIZE = 20;

export function ZasilkyPage() {
  useScreenView('Baleni', 'BaleniShipments');
  const { showSuccess, showError } = useToast();
  const [filters, setFilters] = useState<FilterValues>({
    orderCode: "",
    customerName: "",
    packageNumber: "",
    shippingProviderCode: "",
    fromDate: "",
    toDate: "",
  });
  const [pageNumber, setPageNumber] = useState(1);
  const [sortBy, setSortBy] = useState<ZasilkySortBy>("PackedAt");
  const [sortDescending, setSortDescending] = useState(true);
  const [pendingDelete, setPendingDelete] = useState<PackageDto | null>(null);

  const request = useMemo(
    () => ({
      orderCode: filters.orderCode || undefined,
      customerName: filters.customerName || undefined,
      packageNumber: filters.packageNumber || undefined,
      shippingProviderCode: filters.shippingProviderCode || undefined,
      fromDate: filters.fromDate || undefined,
      toDate: filters.toDate || undefined,
      pageNumber,
      pageSize: PAGE_SIZE,
      sortBy,
      sortDescending,
    }),
    [filters, pageNumber, sortBy, sortDescending],
  );

  const { data, isLoading, isError } = usePackagesQuery(request);
  const deleteMutation = useDeletePackageMutation();

  const handleSortChange = useCallback(
    (col: ZasilkySortBy) => {
      if (col === sortBy) {
        setSortDescending((d) => !d);
      } else {
        setSortBy(col);
        setSortDescending(true);
      }
    },
    [sortBy],
  );

  const handleFiltersChange = useCallback((next: FilterValues) => {
    setFilters(next);
    setPageNumber(1);
  }, []);

  const handleReprint = useCallback(
    (pkg: PackageDto) => {
      printLabelPdf(pkg.orderCode, { packageName: pkg.packageNumber }, () => {
        showSuccess("Tisk", `Štítek balíku ${pkg.packageNumber} odeslán na tiskárnu.`);
      });
    },
    [showSuccess],
  );

  const confirmDelete = useCallback(async () => {
    if (!pendingDelete) return;
    try {
      await deleteMutation.mutateAsync(pendingDelete.id);
      showSuccess("Smazáno", `Zásilka ${pendingDelete.packageNumber} byla smazána.`);
      setPendingDelete(null);
    } catch (e) {
      showError("Chyba", e instanceof Error ? e.message : "Smazání selhalo.");
    }
  }, [deleteMutation, pendingDelete, showSuccess, showError]);

  return (
    <div className="flex flex-col h-full bg-white">
      <ZasilkyFilters value={filters} onChange={handleFiltersChange} />
      <div className="flex-1 overflow-auto">
        {isLoading && <div className="p-8 text-center text-slate-500">Načítám…</div>}
        {isError && (
          <div className="p-8 text-center text-red-600">Nepodařilo se načíst zásilky.</div>
        )}
        {data && (
          <ZasilkyTable
            items={data.items}
            sortBy={sortBy}
            sortDescending={sortDescending}
            onSortChange={handleSortChange}
            onReprint={handleReprint}
            onDelete={setPendingDelete}
          />
        )}
      </div>
      {data && (
        <ZasilkyPagination
          pageNumber={pageNumber}
          pageSize={PAGE_SIZE}
          totalCount={data.totalCount}
          onPageChange={setPageNumber}
        />
      )}
      <DeletePackageDialog
        pkg={pendingDelete}
        isDeleting={deleteMutation.isPending}
        onConfirm={confirmDelete}
        onCancel={() => setPendingDelete(null)}
      />
    </div>
  );
}
