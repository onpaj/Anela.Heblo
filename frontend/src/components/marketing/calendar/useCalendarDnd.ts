import { useState, useCallback, useMemo } from 'react';
import {
  useSensors,
  useSensor,
  PointerSensor,
  TouchSensor,
  type DragStartEvent,
  type DragOverEvent,
  type DragEndEvent,
} from '@dnd-kit/core';
import type { CalendarEvent } from './useCalendarLayout';
import type { CalendarDragData, DayDropData, CalendarDndCallbacks } from './calendarDndTypes';
import { addDaysToDateString, daysBetween, clampDateString } from './calendarDateUtils';

interface DragState {
  type: 'move' | 'resize-start' | 'resize-end';
  eventId: number;
  event: CalendarEvent;
  originDate: string;
  dateDelta: number;
}

interface PreviewInput {
  type: 'move' | 'resize-start' | 'resize-end';
  eventId: number;
  dateDelta: number;
}

export function computePreviewEvents(
  events: readonly CalendarEvent[],
  drag: PreviewInput | null,
): CalendarEvent[] {
  if (!drag) return events as CalendarEvent[];

  return events.map((ev) => {
    if (ev.id !== drag.eventId) return ev;

    switch (drag.type) {
      case 'move':
        return {
          ...ev,
          dateFrom: addDaysToDateString(ev.dateFrom, drag.dateDelta),
          dateTo: addDaysToDateString(ev.dateTo, drag.dateDelta),
        };
      case 'resize-start': {
        const newFrom = addDaysToDateString(ev.dateFrom, drag.dateDelta);
        return { ...ev, dateFrom: clampDateString(newFrom, ev.dateTo) };
      }
      case 'resize-end': {
        const newTo = addDaysToDateString(ev.dateTo, drag.dateDelta);
        return { ...ev, dateTo: newTo < ev.dateFrom ? ev.dateFrom : newTo };
      }
      default:
        return ev;
    }
  });
}

export function useCalendarDnd(events: CalendarEvent[], callbacks: CalendarDndCallbacks) {
  const [dragState, setDragState] = useState<DragState | null>(null);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 5 } }),
  );

  const previewEvents = useMemo(
    () => computePreviewEvents(events, dragState),
    [events, dragState],
  );

  const handleDragStart = useCallback((event: DragStartEvent) => {
    const data = event.active.data.current as CalendarDragData;
    setDragState({
      type: data.type,
      eventId: data.eventId,
      event: data.event,
      originDate: data.type === 'move' ? data.originDate : data.event.dateFrom,
      dateDelta: 0,
    });
  }, []);

  const handleDragOver = useCallback(
    (event: DragOverEvent) => {
      if (!dragState || !event.over) return;
      const dropData = event.over.data.current as DayDropData | undefined;
      if (!dropData?.date) return;

      const hoverDate = dropData.date;
      let delta: number;

      if (dragState.type === 'move') {
        delta = daysBetween(dragState.originDate, hoverDate);
      } else if (dragState.type === 'resize-start') {
        delta = daysBetween(dragState.event.dateFrom, hoverDate);
      } else {
        delta = daysBetween(dragState.event.dateTo, hoverDate);
      }

      if (delta !== dragState.dateDelta) {
        setDragState((prev) => (prev ? { ...prev, dateDelta: delta } : null));
      }
    },
    [dragState],
  );

  const handleDragEnd = useCallback(
    (_event: DragEndEvent) => {
      if (!dragState || dragState.dateDelta === 0) {
        setDragState(null);
        return;
      }

      const { type, eventId, event: ev, dateDelta } = dragState;

      if (type === 'move') {
        callbacks.onEventMove(
          eventId,
          addDaysToDateString(ev.dateFrom, dateDelta),
          addDaysToDateString(ev.dateTo, dateDelta),
        );
      } else if (type === 'resize-start') {
        const newFrom = clampDateString(addDaysToDateString(ev.dateFrom, dateDelta), ev.dateTo);
        callbacks.onEventResize(eventId, newFrom, ev.dateTo);
      } else {
        const newTo = addDaysToDateString(ev.dateTo, dateDelta);
        callbacks.onEventResize(eventId, ev.dateFrom, newTo < ev.dateFrom ? ev.dateFrom : newTo);
      }

      setDragState(null);
    },
    [dragState, callbacks],
  );

  const handleDragCancel = useCallback(() => setDragState(null), []);

  return {
    sensors,
    dragState,
    previewEvents,
    handleDragStart,
    handleDragOver,
    handleDragEnd,
    handleDragCancel,
  };
}
