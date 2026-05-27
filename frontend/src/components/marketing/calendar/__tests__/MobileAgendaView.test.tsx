import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { MobileAgendaView } from '../MobileAgendaView';

// Mocked AgendaDayGroup renders a simple div to let us count day groups
jest.mock('../AgendaDayGroup', () => ({
  AgendaDayGroup: ({ day, isToday, onEventClick }: any) => (
    <div
      data-testid={`day-group-${day.date}`}
      data-is-today={String(isToday)}
    >
      {day.events.map((e: any) => (
        <button key={e.id} onClick={() => onEventClick(e.id)}>
          {e.title}
        </button>
      ))}
    </div>
  ),
}));

jest.mock('../../detail/MarketingActionModal', () => ({
  __esModule: true,
  default: ({ isOpen, existingAction, prefillDates, onClose }: any) =>
    isOpen ? (
      <div data-testid="marketing-modal">
        <span data-testid="modal-existing-id">{existingAction?.id ?? ''}</span>
        <span data-testid="modal-prefill-from">{prefillDates?.dateFrom ?? ''}</span>
        <button onClick={onClose}>Zrušit</button>
      </div>
    ) : null,
}));

jest.mock('../../../manufacture/calendar/CalendarNavigation', () => ({
  __esModule: true,
  default: ({ onPrevious, onNext, onToday }: any) => (
    <div data-testid="calendar-navigation">
      <button data-testid="nav-prev" onClick={onPrevious}>Prev</button>
      <button data-testid="nav-today" onClick={onToday}>Dnes</button>
      <button data-testid="nav-next" onClick={onNext}>Next</button>
    </div>
  ),
}));

// Module-level controls so individual tests can override behaviour
let mockIsLoading = false;
let mockError: Error | null = null;
const mockRefetch = jest.fn();
let mockDetailData: any = null;
let mockCalendarData: any = { actions: [] };

jest.mock('../../../../api/hooks/useMarketingCalendar', () => ({
  useMarketingCalendar: () => ({
    data: mockCalendarData,
    isLoading: mockIsLoading,
    error: mockError,
    refetch: mockRefetch,
  }),
  useMarketingAction: () => ({ data: mockDetailData }),
}));

beforeEach(() => {
  mockIsLoading = false;
  mockError = null;
  mockDetailData = null;
  mockCalendarData = { actions: [] };
  mockRefetch.mockClear();
});

describe('MobileAgendaView', () => {
  it('renders the "Kalendář" heading', () => {
    render(<MobileAgendaView />);
    expect(screen.getByText('Kalendář')).toBeInTheDocument();
  });

  it('renders exactly 14 day groups', () => {
    render(<MobileAgendaView />);
    expect(screen.getAllByTestId(/^day-group-/)).toHaveLength(14);
  });

  it('renders CalendarNavigation', () => {
    render(<MobileAgendaView />);
    expect(screen.getByTestId('calendar-navigation')).toBeInTheDocument();
  });

  it('shows loading state while fetching', () => {
    mockIsLoading = true;
    render(<MobileAgendaView />);
    expect(screen.getByText('Načítání...')).toBeInTheDocument();
    expect(screen.queryByTestId(/^day-group-/)).not.toBeInTheDocument();
  });

  it('shows inline error message and retry button on fetch failure', () => {
    mockError = new Error('network failure');
    render(<MobileAgendaView />);
    expect(screen.getByText('Chyba při načítání akcí.')).toBeInTheDocument();
    expect(screen.getByText('Zkusit znovu')).toBeInTheDocument();
    expect(screen.queryByTestId(/^day-group-/)).not.toBeInTheDocument();
  });

  it('retry button calls refetch', () => {
    mockError = new Error('fail');
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByText('Zkusit znovu'));
    expect(mockRefetch).toHaveBeenCalledTimes(1);
  });

  it('+ button opens the create modal with today as prefill date', () => {
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByLabelText('Nová akce'));
    expect(screen.getByTestId('marketing-modal')).toBeInTheDocument();
    const today = new Date();
    const todayStr = [
      today.getFullYear(),
      String(today.getMonth() + 1).padStart(2, '0'),
      String(today.getDate()).padStart(2, '0'),
    ].join('-');
    expect(screen.getByTestId('modal-prefill-from')).toHaveTextContent(todayStr);
    expect(screen.getByTestId('modal-existing-id')).toHaveTextContent('');
  });

  it('Zrušit closes the modal', () => {
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByLabelText('Nová akce'));
    expect(screen.getByTestId('marketing-modal')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Zrušit'));
    expect(screen.queryByTestId('marketing-modal')).not.toBeInTheDocument();
  });

  it('prev button shifts the window back by 14 days', () => {
    render(<MobileAgendaView />);
    const before = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    fireEvent.click(screen.getByTestId('nav-prev'));
    const after = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    expect(after).toHaveLength(14);
    expect(after).not.toEqual(before);
  });

  it('next button shifts the window forward by 14 days', () => {
    render(<MobileAgendaView />);
    const before = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    fireEvent.click(screen.getByTestId('nav-next'));
    const after = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    expect(after).toHaveLength(14);
    expect(after).not.toEqual(before);
  });

  it('today button resets the window to current week', () => {
    render(<MobileAgendaView />);
    fireEvent.click(screen.getByTestId('nav-prev')); // navigate away
    const displaced = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    fireEvent.click(screen.getByTestId('nav-today'));
    const reset = screen.getAllByTestId(/^day-group-/).map((el) => el.dataset.testid!);
    expect(reset).not.toEqual(displaced);
  });

  it('tapping an event card sets selectedActionId and opens edit modal', async () => {
    // Provide one event so the mocked AgendaDayGroup can render a clickable button
    mockCalendarData = {
      actions: [
        {
          id: 42,
          title: 'Prodejní kampaň',
          actionType: 'SocialMedia',
          dateFrom: new Date().toISOString().slice(0, 10),
          dateTo: new Date().toISOString().slice(0, 10),
          associatedProducts: [],
        },
      ],
    };
    // Provide full action data so useEffect can populate editingAction
    mockDetailData = {
      action: {
        id: 42,
        title: 'Prodejní kampaň',
        actionType: 'SocialMedia',
        startDate: new Date().toISOString().slice(0, 10),
        endDate: new Date().toISOString().slice(0, 10),
        associatedProducts: [],
        folderLinks: [],
      },
    };

    render(<MobileAgendaView />);

    // The mocked AgendaDayGroup renders a <button> for each event in day.events
    const eventBtn = screen.getByText('Prodejní kampaň');
    fireEvent.click(eventBtn);

    // Modal should open
    expect(screen.getByTestId('marketing-modal')).toBeInTheDocument();
  });
});
