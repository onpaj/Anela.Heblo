import React, { useState, useMemo, useEffect } from 'react';
import { Plus } from 'lucide-react';
import CalendarNavigation from '../../manufacture/calendar/CalendarNavigation';
import MarketingActionModal from '../detail/MarketingActionModal';
import { useMarketingCalendar, useMarketingAction } from '../../../api/hooks/useMarketingCalendar';
import { formatDateStr } from './fullcalendarAdapters';
import type { CalendarEvent } from './fullcalendarAdapters';
import { groupEventsByDay } from './agendaGrouping';
import { AgendaDayGroup } from './AgendaDayGroup';
import type { MarketingActionDto } from '../list/MarketingActionGrid';
import './mobileAgenda.css';

const CZECH_MONTHS = [
  'Leden', 'Únor', 'Březen', 'Duben', 'Květen', 'Červen',
  'Červenec', 'Srpen', 'Září', 'Říjen', 'Listopad', 'Prosinec',
];

function startOfWeekMonday(date: Date): Date {
  const clone = new Date(date);
  const dow = clone.getDay();
  const daysToMonday = dow === 0 ? 6 : dow - 1;
  clone.setHours(0, 0, 0, 0);
  clone.setDate(clone.getDate() - daysToMonday);
  clone.setHours(0, 0, 0, 0);
  return clone;
}

function addDays(date: Date, n: number): Date {
  const clone = new Date(date);
  clone.setDate(clone.getDate() + n);
  return clone;
}

function buildPeriodLabel(start: Date, end: Date): string {
  const sm = start.getMonth();
  const sy = start.getFullYear();
  const em = end.getMonth();
  const ey = end.getFullYear();
  if (sy === ey && sm === em) return `${CZECH_MONTHS[sm]} ${sy}`;
  if (sy === ey) return `${CZECH_MONTHS[sm]} – ${CZECH_MONTHS[em]} ${sy}`;
  return `${CZECH_MONTHS[sm]} ${sy} – ${CZECH_MONTHS[em]} ${ey}`;
}

export function MobileAgendaView() {
  const [windowStart, setWindowStart] = useState(() => startOfWeekMonday(new Date()));
  const windowEnd = useMemo(() => addDays(windowStart, 13), [windowStart]);

  const [selectedActionId, setSelectedActionId] = useState<number | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingAction, setEditingAction] = useState<MarketingActionDto | null>(null);
  const [prefillDates, setPrefillDates] = useState<{ dateFrom: string; dateTo: string } | null>(null);

  const { data, isLoading, error, refetch } = useMarketingCalendar({
    startDate: windowStart,
    endDate: windowEnd,
  });

  // useMarketingAction has enabled: id > 0, so passing 0 when nothing is selected is a no-op
  const detailQuery = useMarketingAction(selectedActionId ?? 0);

  useEffect(() => {
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

  const calendarEvents: CalendarEvent[] = useMemo(
    () =>
      ((data as any)?.actions ?? []).map((a: any) => ({
        id: a.id!,
        title: a.title ?? '',
        actionType: a.actionType ?? 'Other',
        dateFrom: a.startDate instanceof Date ? formatDateStr(a.startDate) : (a.dateFrom ?? ''),
        dateTo: a.endDate instanceof Date ? formatDateStr(a.endDate) : (a.dateTo ?? ''),
        associatedProducts: a.associatedProducts ?? [],
        outlookSyncStatus: a.outlookSyncStatus,
      })),
    [data],
  );

  const startStr = formatDateStr(windowStart);
  const endStr = formatDateStr(windowEnd);

  const agendaDays = useMemo(
    () => groupEventsByDay(calendarEvents, startStr, endStr),
    [calendarEvents, startStr, endStr],
  );

  const todayStr = formatDateStr(new Date());

  const openCreate = () => {
    // Mobile UX: pre-fill today's date so the user can tap + and save immediately
    setPrefillDates({ dateFrom: todayStr, dateTo: todayStr });
    setEditingAction(null);
    setSelectedActionId(null);
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

  const periodLabel = buildPeriodLabel(windowStart, windowEnd);

  return (
    <div className="mobile-agenda">
      <div className="mobile-agenda__header">
        <h1 className="mobile-agenda__title">Kalendář</h1>
        <button
          type="button"
          className="mobile-agenda__create-btn"
          onClick={openCreate}
          aria-label="Nová akce"
        >
          <Plus className="h-5 w-5" />
        </button>
      </div>

      <div className="mobile-agenda__nav">
        <CalendarNavigation
          onPrevious={() => setWindowStart((d) => addDays(d, -14))}
          onNext={() => setWindowStart((d) => addDays(d, 14))}
          onToday={() => setWindowStart(startOfWeekMonday(new Date()))}
          currentPeriodLabel={periodLabel}
          size="sm"
        />
      </div>

      <div className="mobile-agenda__scroll">
        {isLoading ? (
          <div className="mobile-agenda__loading">Načítání...</div>
        ) : error ? (
          <div className="mobile-agenda__error">
            <p>Chyba při načítání akcí.</p>
            <button
              type="button"
              className="mobile-agenda__retry-btn"
              onClick={() => refetch()}
            >
              Zkusit znovu
            </button>
          </div>
        ) : (
          agendaDays.map((day) => (
            <AgendaDayGroup
              key={day.date}
              day={day}
              isToday={day.date === todayStr}
              onEventClick={openEdit}
            />
          ))
        )}
      </div>

      <MarketingActionModal
        isOpen={isModalOpen}
        onClose={closeModal}
        existingAction={editingAction}
        prefillDates={prefillDates}
      />
    </div>
  );
}
