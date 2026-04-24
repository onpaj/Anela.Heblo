import React, { useState, useMemo, useRef, useCallback } from 'react';
import { Plus, Calendar, List } from 'lucide-react';
import FullCalendar from '@fullcalendar/react';
import CalendarNavigation from '../../manufacture/calendar/CalendarNavigation';
import MarketingMonthCalendar from '../calendar/MarketingMonthCalendar';
import MarketingActionGrid from '../list/MarketingActionGrid';
import type { MarketingActionDto } from '../list/MarketingActionGrid';
import MarketingActionFilters, {
  EMPTY_FILTERS,
  type MarketingFilters,
} from '../list/MarketingActionFilters';
import MarketingActionModal from '../detail/MarketingActionModal';
import {
  useMarketingCalendar,
  useMarketingActions,
  useMarketingAction,
  useUpdateMarketingAction,
} from '../../../api/hooks/useMarketingCalendar';
import { ACTION_TYPE_TO_INT } from '../calendar/fullcalendarAdapters';
import type { CalendarEvent } from '../calendar/fullcalendarAdapters';
import { PAGE_CONTAINER_HEIGHT } from '../../../constants/layout';

const CZECH_MONTHS = [
  'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
  'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
];

type ViewMode = 'calendar' | 'list';

const MarketingCalendarPage: React.FC = () => {
  const [viewMode, setViewMode] = useState<ViewMode>('calendar');
  const [currentDate, setCurrentDate] = useState(new Date());
  const [visibleRange, setVisibleRange] = useState<{ start: Date; end: Date } | null>(null);
  const [filters, setFilters] = useState<MarketingFilters>(EMPTY_FILTERS);
  const [pageNumber, setPageNumber] = useState(1);
  const [selectedActionId, setSelectedActionId] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingAction, setEditingAction] = useState<MarketingActionDto | null>(null);
  const [prefillDates, setPrefillDates] = useState<{ dateFrom: string; dateTo: string } | null>(null);

  const calendarRef = useRef<FullCalendar>(null);

  const year = currentDate.getFullYear();
  const month = currentDate.getMonth();

  // API query range comes from FullCalendar's visible range (set via datesSet callback).
  // Fall back to a manual calculation on first render before datesSet fires.
  const { startDate, endDate } = useMemo(() => {
    if (visibleRange) {
      return { startDate: visibleRange.start, endDate: visibleRange.end };
    }
    const first = new Date(year, month, 1);
    const firstMonday = new Date(first);
    const dow = first.getDay();
    firstMonday.setDate(first.getDate() + (dow === 0 ? -6 : 1 - dow));
    const lastSunday = new Date(firstMonday);
    lastSunday.setDate(firstMonday.getDate() + 41);
    return { startDate: firstMonday, endDate: lastSunday };
  }, [visibleRange, year, month]);

  const calendarQuery = useMarketingCalendar({ startDate, endDate });
  const listQuery = useMarketingActions({
    pageNumber,
    searchTerm: filters.searchText || undefined,
    startDateFrom: filters.dateFrom ? new Date(filters.dateFrom) : undefined,
    startDateTo: filters.dateTo ? new Date(filters.dateTo) : undefined,
  });
  const detailQuery = useMarketingAction(selectedActionId ?? 0);
  const updateMutation = useUpdateMarketingAction();

  const calendarEvents: CalendarEvent[] = useMemo(
    () =>
      ((calendarQuery.data as any)?.actions ?? []).map((a: any) => ({
        id: a.id!,
        title: a.title ?? '',
        actionType: a.actionType ?? 'Other',
        dateFrom: String(a.dateFrom ?? a.startDate ?? ''),
        dateTo: String(a.dateTo ?? a.endDate ?? ''),
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

  // CalendarNavigation drives FullCalendar; datesSet callback syncs currentDate back
  const goToPrev = () => calendarRef.current?.getApi().prev();
  const goToNext = () => calendarRef.current?.getApi().next();
  const goToToday = () => calendarRef.current?.getApi().today();

  const handleDatesSet = useCallback(
    (_visibleStart: Date, _visibleEnd: Date, currentStart: Date) => {
      setCurrentDate(new Date(currentStart));
      setVisibleRange({ start: _visibleStart, end: _visibleEnd });
    },
    [],
  );

  const openCreate = () => {
    setPrefillDates(null);
    setEditingAction(null);
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

  const handleEventMove = useCallback(
    (id: number, dateFrom: string, dateTo: string) => {
      const event = calendarEvents.find((e) => e.id === id);
      if (!event) return;
      updateMutation.mutate({
        id,
        request: {
          title: event.title,
          actionType: ACTION_TYPE_TO_INT[event.actionType] ?? 99,
          startDate: new Date(dateFrom),
          endDate: new Date(dateTo),
          associatedProducts: event.associatedProducts,
        },
      });
    },
    [calendarEvents, updateMutation],
  );

  const handleEventResize = useCallback(
    (id: number, dateFrom: string, dateTo: string) => {
      handleEventMove(id, dateFrom, dateTo);
    },
    [handleEventMove],
  );

  const handleDateRangeSelect = useCallback(
    (dateFrom: string, dateTo: string) => {
      setPrefillDates({ dateFrom, dateTo });
      setEditingAction(null);
      setIsModalOpen(true);
    },
    [],
  );

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
              onClick={() => setViewMode('calendar')}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === 'calendar'
                  ? 'bg-indigo-600 text-white'
                  : 'text-gray-600 hover:bg-gray-50'
              }`}
            >
              <Calendar className="h-4 w-4" />
              Kalendář
            </button>
            <button
              onClick={() => setViewMode('list')}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === 'list'
                  ? 'bg-indigo-600 text-white'
                  : 'text-gray-600 hover:bg-gray-50'
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
        {viewMode === 'calendar' ? (
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
                  events={calendarEvents}
                  initialDate={currentDate}
                  onEventClick={openEdit}
                  onEventMove={handleEventMove}
                  onEventResize={handleEventResize}
                  onDateRangeSelect={handleDateRangeSelect}
                  onDatesSet={handleDatesSet}
                  calendarRef={calendarRef}
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
