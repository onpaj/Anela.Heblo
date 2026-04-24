import React, { useState, useMemo, useCallback } from "react";
import { Plus, Calendar, List } from "lucide-react";
import CalendarNavigation from "../../manufacture/calendar/CalendarNavigation";
import MarketingMonthCalendar from "../calendar/MarketingMonthCalendar";
import MarketingActionGrid from "../list/MarketingActionGrid";
import type { MarketingActionDto } from "../list/MarketingActionGrid";
import MarketingActionFilters, {
  EMPTY_FILTERS,
  type MarketingFilters,
} from "../list/MarketingActionFilters";
import MarketingActionModal from "../detail/MarketingActionModal";
import {
  useMarketingCalendar,
  useMarketingActions,
  useMarketingAction,
  useUpdateMarketingAction,
} from "../../../api/hooks/useMarketingCalendar";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";

const CZECH_MONTHS = [
  "Leden",
  "Únor",
  "Březen",
  "Duben",
  "Květen",
  "Červen",
  "Červenec",
  "Srpen",
  "Září",
  "Říjen",
  "Listopad",
  "Prosinec",
];

type ViewMode = "calendar" | "list";

const ACTION_TYPE_TO_INT: Record<string, number> = {
  General: 0,
  SocialMedia: 0,
  Promotion: 1,
  Launch: 2,
  Email: 2,
  Campaign: 3,
  PR: 3,
  Event: 4,
  Photoshoot: 4,
  Other: 99,
};

const resolveActionTypeToInt = (actionType: string): number =>
  ACTION_TYPE_TO_INT[actionType] ?? 99;

const MarketingCalendarPage: React.FC = () => {
  const [viewMode, setViewMode] = useState<ViewMode>("calendar");
  const [currentDate, setCurrentDate] = useState(new Date());
  const [filters, setFilters] = useState<MarketingFilters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [selectedActionId, setSelectedActionId] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingAction, setEditingAction] = useState<MarketingActionDto | null>(
    null,
  );
  const [prefillDates, setPrefillDates] = useState<{ dateFrom: string; dateTo: string } | null>(null);
  const updateMutation = useUpdateMarketingAction();

  const year = currentDate.getFullYear();
  const month = currentDate.getMonth();

  // Calendar query — first and last day of the displayed month grid
  const { startDate, endDate } = useMemo(() => {
    const first = new Date(year, month, 1);
    const firstMonday = new Date(first);
    const dow = first.getDay();
    firstMonday.setDate(first.getDate() + (dow === 0 ? -6 : 1 - dow));
    const lastSunday = new Date(firstMonday);
    lastSunday.setDate(firstMonday.getDate() + 41);
    return {
      startDate: firstMonday,
      endDate: lastSunday,
    };
  }, [year, month]);

  const calendarQuery = useMarketingCalendar({ startDate, endDate });
  const listQuery = useMarketingActions({
    pageNumber,
    searchTerm: filters.searchText || undefined,
    startDateFrom: filters.dateFrom ? new Date(filters.dateFrom) : undefined,
    startDateTo: filters.dateTo ? new Date(filters.dateTo) : undefined,
  });
  const detailQuery = useMarketingAction(selectedActionId ?? 0);

  const calendarEvents = useMemo(
    () =>
      ((calendarQuery.data as any)?.actions ?? []).map((a: any) => ({
        id: a.id!,
        title: a.title ?? "",
        actionType: a.actionType ?? "Other",
        dateFrom: String(a.dateFrom ?? a.startDate ?? ""),
        dateTo: String(a.dateTo ?? a.endDate ?? ""),
        associatedProducts: a.associatedProducts ?? [],
      })),
    [calendarQuery.data],
  );

  const listActions: MarketingActionDto[] = useMemo(
    () =>
      ((listQuery.data as any)?.actions ?? []).map((a: any) => ({
        id: a.id,
        title: a.title,
        detail: a.description,
        actionType: a.actionType,
        dateFrom: a.startDate ?? a.dateFrom,
        dateTo: a.endDate ?? a.dateTo,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
      })),
    [listQuery.data],
  );

  const totalPages: number = (listQuery.data as any)?.totalPages ?? 1;

  const periodLabel = `${CZECH_MONTHS[month]} ${year}`;

  const goToPrev = () =>
    setCurrentDate((d) => new Date(d.getFullYear(), d.getMonth() - 1, 1));
  const goToNext = () =>
    setCurrentDate((d) => new Date(d.getFullYear(), d.getMonth() + 1, 1));
  const goToToday = () => setCurrentDate(new Date());

  const openCreate = () => {
    setEditingAction(null);
    setPrefillDates(null);
    setIsModalOpen(true);
  };

  const openEdit = (id: number) => {
    setSelectedActionId(id);
    setIsModalOpen(true);
  };

  const closeModal = () => {
    setIsModalOpen(false);
    setEditingAction(null);
    setSelectedActionId(null);
    setPrefillDates(null);
  };

  const handleEventMove = useCallback(
    (eventId: number, newDateFrom: string, newDateTo: string) => {
      const event = calendarEvents.find((e: any) => e.id === eventId);
      if (!event) return;
      updateMutation.mutate({
        id: eventId,
        request: {
          title: event.title,
          actionType: resolveActionTypeToInt(event.actionType),
          startDate: new Date(newDateFrom),
          endDate: new Date(newDateTo),
          associatedProducts: event.associatedProducts,
        },
      });
    },
    [calendarEvents, updateMutation],
  );

  const handleEventResize = useCallback(
    (eventId: number, newDateFrom: string, newDateTo: string) => {
      const event = calendarEvents.find((e: any) => e.id === eventId);
      if (!event) return;
      updateMutation.mutate({
        id: eventId,
        request: {
          title: event.title,
          actionType: resolveActionTypeToInt(event.actionType),
          startDate: new Date(newDateFrom),
          endDate: new Date(newDateTo),
          associatedProducts: event.associatedProducts,
        },
      });
    },
    [calendarEvents, updateMutation],
  );

  const handleCreateRange = useCallback(
    (dateFrom: string, dateTo: string) => {
      setPrefillDates({ dateFrom, dateTo });
      setEditingAction(null);
      setIsModalOpen(true);
    },
    [],
  );

  // Sync detail data into editingAction when it arrives
  React.useEffect(() => {
    if ((detailQuery.data as any)?.action) {
      const a = (detailQuery.data as any).action;
      setEditingAction({
        id: a.id,
        title: a.title,
        detail: a.description ?? a.detail,
        actionType: a.actionType,
        dateFrom: a.startDate ?? a.dateFrom,
        dateTo: a.endDate ?? a.dateTo,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
      });
    }
  }, [detailQuery.data]);

  return (
    <div className="flex flex-col" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      {/* Toolbar */}
      <div className="flex items-center justify-between px-6 py-4 bg-white border-b border-gray-200 flex-shrink-0">
        <h1 className="text-xl font-semibold text-gray-900">
          Marketingový kalendář
        </h1>
        <div className="flex items-center gap-3">
          {/* View toggle */}
          <div className="flex border border-gray-200 rounded-lg overflow-hidden">
            <button
              onClick={() => setViewMode("calendar")}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === "calendar"
                  ? "bg-indigo-600 text-white"
                  : "text-gray-600 hover:bg-gray-50"
              }`}
            >
              <Calendar className="h-4 w-4" />
              Kalendář
            </button>
            <button
              onClick={() => setViewMode("list")}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === "list"
                  ? "bg-indigo-600 text-white"
                  : "text-gray-600 hover:bg-gray-50"
              }`}
            >
              <List className="h-4 w-4" />
              Seznam
            </button>
          </div>
          <button
            onClick={openCreate}
            className="flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors"
          >
            <Plus className="h-4 w-4" />
            Nová akce
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-6">
        {viewMode === "calendar" ? (
          <div className="flex flex-col h-full gap-4">
            <div className="flex-shrink-0">
              <CalendarNavigation
                onPrevious={goToPrev}
                onNext={goToNext}
                onToday={goToToday}
                currentPeriodLabel={periodLabel}
              />
            </div>
            {calendarQuery.isLoading ? (
              <div className="text-center py-12 text-gray-500 text-sm">
                Načítání...
              </div>
            ) : calendarQuery.error ? (
              <div className="text-center py-12 text-red-500 text-sm">
                Chyba při načítání kalendáře.
              </div>
            ) : (
              <div className="flex-1 min-h-0">
                <MarketingMonthCalendar
                  year={year}
                  month={month}
                  events={calendarEvents}
                  onEventClick={openEdit}
                  onEventMove={handleEventMove}
                  onEventResize={handleEventResize}
                  onCreateRange={handleCreateRange}
                  className="h-full"
                />
              </div>
            )}
          </div>
        ) : (
          <div className="space-y-4">
            <MarketingActionFilters
              filters={filters}
              onChange={(f) => {
                setFilters(f);
                setPageNumber(1);
              }}
              onClear={() => {
                setFilters(EMPTY_FILTERS);
                setPageNumber(1);
              }}
            />
            <div className="bg-white border border-gray-200 rounded-lg overflow-hidden">
              <MarketingActionGrid
                actions={listActions}
                totalPages={totalPages}
                pageNumber={pageNumber}
                onPageChange={setPageNumber}
                onActionClick={openEdit}
                isLoading={listQuery.isLoading}
              />
            </div>
          </div>
        )}
      </div>

      {/* Modal */}
      <MarketingActionModal
        isOpen={isModalOpen}
        onClose={closeModal}
        existingAction={editingAction}
        prefillDates={prefillDates}
      />
    </div>
  );
};

export default MarketingCalendarPage;
