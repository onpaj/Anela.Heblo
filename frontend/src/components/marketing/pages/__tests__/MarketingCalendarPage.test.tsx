import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import MarketingCalendarPage from "../MarketingCalendarPage";

// Track every render of the calendar mock so tests can verify mount/unmount and the props it receives.
const calendarRenderLog: { viewName: string; initialDate: Date; mountId: number }[] = [];

jest.mock("../../calendar/MarketingMonthCalendar", () => {
  const React = require("react");
  let calendarMountCounter = 0;
  const mockGotoDate = jest.fn();
  function MarketingMonthCalendarMock(props) {
    const mountId = React.useMemo(() => ++calendarMountCounter, []);
    React.useEffect(() => {
      calendarRenderLog.push({
        viewName: props.viewName,
        initialDate: new Date(props.initialDate),
        mountId,
      });
      if (props.calendarRef) {
        props.calendarRef.current = {
          getApi: () => ({
            prev: jest.fn(),
            next: jest.fn(),
            gotoDate: mockGotoDate,
          }),
        };
      }
    });
    return React.createElement("div", {
      "data-testid": "marketing-month-calendar",
      "data-view-name": props.viewName,
      "data-mount-id": String(mountId),
      "data-initial-date": props.initialDate.toISOString(),
    });
  }
  return {
    __esModule: true,
    default: MarketingMonthCalendarMock,
    __mockGotoDate: mockGotoDate,
  };
});

const mockGotoDate = require("../../calendar/MarketingMonthCalendar").__mockGotoDate;

jest.mock("../../detail/MarketingActionModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../detail/ImportFromOutlookModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../list/MarketingActionGrid", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: () => React.createElement("div", { "data-testid": "marketing-action-grid" }),
  };
});

jest.mock("../../list/MarketingActionFilters", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: () => React.createElement("div", { "data-testid": "marketing-action-filters" }),
    EMPTY_FILTERS: { searchText: "", dateFrom: "", dateTo: "", actionType: "" },
  };
});

jest.mock("../../../manufacture/calendar/CalendarNavigation", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: ({ onPrevious, onNext, onToday }) =>
      React.createElement(
        "div",
        { "data-testid": "calendar-navigation" },
        React.createElement(
          "button",
          { "data-testid": "nav-prev", onClick: onPrevious },
          "Prev"
        ),
        React.createElement(
          "button",
          { "data-testid": "nav-today", onClick: onToday },
          "Dnes"
        ),
        React.createElement(
          "button",
          { "data-testid": "nav-next", onClick: onNext },
          "Next"
        )
      ),
  };
});

// Capture every call to useMarketingCalendar so we can assert the fetch range.
const calendarHookCalls: { startDate: Date; endDate: Date }[] = [];

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useMarketingCalendar: (args) => {
    calendarHookCalls.push({
      startDate: new Date(args.startDate),
      endDate: new Date(args.endDate),
    });
    return { data: { actions: [] }, isLoading: false, error: null };
  },
  useMarketingActions: () => ({
    data: { actions: [], totalPages: 1 },
    isLoading: false,
    error: null,
  }),
  useMarketingAction: () => ({ data: null, isLoading: false, error: null }),
  useUpdateMarketingAction: () => ({ mutate: jest.fn() }),
}));

jest.mock("../../../../auth/useAuth", () => ({
  useAuth: () => ({
    getUserInfo: () => ({ roles: [] }),
  }),
}));

let mockIsMobile = false;
jest.mock("../../../../hooks/useMediaQuery", () => ({
  useIsMobile: () => mockIsMobile,
}));
jest.mock("../../calendar/MobileAgendaView", () => {
  const React = require("react");
  return {
    MobileAgendaView: () => React.createElement("div", { "data-testid": "mobile-agenda-view" }),
  };
});

beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
  mockGotoDate.mockClear();
  mockIsMobile = false;
});

describe("MarketingCalendarPage — default render", () => {
  it("renders the page title", () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByText("Marketingový kalendář")).toBeInTheDocument();
  });
});

describe("MarketingCalendarPage — toolbar", () => {
  it("renders all three view buttons with Czech labels", () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByRole("button", { name: /5 týdnů/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /14 dní/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Seznam/ })).toBeInTheDocument();
  });

  it("highlights '5 týdnů' as the default active view", () => {
    render(<MarketingCalendarPage />);
    const fiveWeeks = screen.getByRole("button", { name: /5 týdnů/ });
    expect(fiveWeeks.className).toContain("bg-indigo-600");
    const twoWeeks = screen.getByRole("button", { name: /14 dní/ });
    const list = screen.getByRole("button", { name: /Seznam/ });
    expect(twoWeeks.className).not.toContain("bg-indigo-600");
    expect(list.className).not.toContain("bg-indigo-600");
  });

  it("renders the calendar with viewName='fiveWeeks' on initial load", () => {
    render(<MarketingCalendarPage />);
    const calendar = screen.getByTestId("marketing-month-calendar");
    expect(calendar.getAttribute("data-view-name")).toBe("fiveWeeks");
  });

  it("clicking '14 dní' remounts the calendar with viewName='twoWeeks'", () => {
    render(<MarketingCalendarPage />);
    const initial = screen.getByTestId("marketing-month-calendar");
    const initialMountId = initial.getAttribute("data-mount-id");

    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));

    const after = screen.getByTestId("marketing-month-calendar");
    expect(after.getAttribute("data-view-name")).toBe("twoWeeks");
    // key={viewMode} forces a fresh mount, so the mount id must change.
    expect(after.getAttribute("data-mount-id")).not.toBe(initialMountId);
  });

  it("clicking 'Seznam' unmounts the calendar and renders the list", () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByTestId("marketing-month-calendar")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));

    expect(
      screen.queryByTestId("marketing-month-calendar"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("marketing-action-grid")).toBeInTheDocument();
  });

  it("returning from Seznam to '5 týdnů' remounts the calendar with viewName='fiveWeeks'", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));
    fireEvent.click(screen.getByRole("button", { name: /5 týdnů/ }));

    const calendar = screen.getByTestId("marketing-month-calendar");
    expect(calendar.getAttribute("data-view-name")).toBe("fiveWeeks");
  });
});

const MS_PER_DAY = 24 * 60 * 60 * 1000;

function daysBetween(start: Date, end: Date): number {
  return Math.round((end.getTime() - start.getTime()) / MS_PER_DAY);
}

function startOfWeekMondayForTest(date: Date): Date {
  const clone = new Date(date);
  const dow = clone.getDay();
  const daysToMonday = dow === 0 ? 6 : dow - 1;
  clone.setHours(0, 0, 0, 0);
  clone.setDate(clone.getDate() - daysToMonday);
  clone.setHours(0, 0, 0, 0);
  return clone;
}

function expectSameInstant(actual: Date, expected: Date) {
  expect(actual.getTime()).toBe(expected.getTime());
}

describe("MarketingCalendarPage — fallback fetch range", () => {
  it("requests a 35-day window for the default fiveWeeks view, anchored one week before current Monday", () => {
    render(<MarketingCalendarPage />);
    expect(calendarHookCalls.length).toBeGreaterThan(0);
    const last = calendarHookCalls[calendarHookCalls.length - 1];
    expect(daysBetween(last.startDate, last.endDate)).toBe(35);

    const currentMonday = startOfWeekMondayForTest(new Date());
    const expected = new Date(currentMonday);
    expected.setDate(expected.getDate() - 7);
    expectSameInstant(last.startDate, expected);
    expect(last.startDate.getHours()).toBe(0);
    expect(last.startDate.getMinutes()).toBe(0);
    expect(last.startDate.getSeconds()).toBe(0);
    expect(last.startDate.getMilliseconds()).toBe(0);
  });

  it("requests a 14-day window anchored on the current Monday after switching to twoWeeks", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    const last = calendarHookCalls[calendarHookCalls.length - 1];
    expect(daysBetween(last.startDate, last.endDate)).toBe(14);

    const expected = startOfWeekMondayForTest(new Date());
    expectSameInstant(last.startDate, expected);
    expect(last.startDate.getHours()).toBe(0);
    expect(last.startDate.getMinutes()).toBe(0);
    expect(last.startDate.getSeconds()).toBe(0);
    expect(last.startDate.getMilliseconds()).toBe(0);
  });
});

describe("MarketingCalendarPage — view-mode toggle resets the anchor", () => {
  it("toggling fiveWeeks → twoWeeks updates initialDate to current Monday", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));

    const lastRender = calendarRenderLog[calendarRenderLog.length - 1];
    expect(lastRender.viewName).toBe("twoWeeks");

    const expected = startOfWeekMondayForTest(new Date());
    expectSameInstant(lastRender.initialDate, expected);
  });

  it("toggling twoWeeks → fiveWeeks updates initialDate to Monday minus 7 days", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    fireEvent.click(screen.getByRole("button", { name: /5 týdnů/ }));

    const lastRender = calendarRenderLog[calendarRenderLog.length - 1];
    expect(lastRender.viewName).toBe("fiveWeeks");

    const expected = startOfWeekMondayForTest(new Date());
    expected.setDate(expected.getDate() - 7);
    expectSameInstant(lastRender.initialDate, expected);
  });

  it("repeated toggling 5w → 14d → 5w → 14d consistently lands on the correct anchor each time", () => {
    render(<MarketingCalendarPage />);
    const currentMonday = startOfWeekMondayForTest(new Date());
    const fiveWeekAnchor = new Date(currentMonday);
    fiveWeekAnchor.setDate(fiveWeekAnchor.getDate() - 7);

    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    expectSameInstant(
      calendarRenderLog[calendarRenderLog.length - 1].initialDate,
      currentMonday,
    );

    fireEvent.click(screen.getByRole("button", { name: /5 týdnů/ }));
    expectSameInstant(
      calendarRenderLog[calendarRenderLog.length - 1].initialDate,
      fiveWeekAnchor,
    );

    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    expectSameInstant(
      calendarRenderLog[calendarRenderLog.length - 1].initialDate,
      currentMonday,
    );
  });

  it("toggling to Seznam does not change initialDate (calendar unmounts, no new render)", () => {
    render(<MarketingCalendarPage />);
    const beforeLen = calendarRenderLog.length;
    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));
    // After switching to list, no further calendar renders should occur.
    expect(calendarRenderLog.length).toBe(beforeLen);
  });
});

describe("MarketingCalendarPage — 'Dnes' button respects active view", () => {
  it("in 14-day mode, clicking 'Dnes' calls gotoDate with current Monday", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    fireEvent.click(screen.getByTestId("nav-today"));

    expect(mockGotoDate).toHaveBeenCalled();
    const arg = mockGotoDate.mock.calls[mockGotoDate.mock.calls.length - 1][0];
    expectSameInstant(arg, startOfWeekMondayForTest(new Date()));
  });

  it("in 5-week mode, clicking 'Dnes' calls gotoDate with Monday minus 7 days", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByTestId("nav-today"));

    expect(mockGotoDate).toHaveBeenCalled();
    const arg = mockGotoDate.mock.calls[mockGotoDate.mock.calls.length - 1][0];
    const expected = startOfWeekMondayForTest(new Date());
    expected.setDate(expected.getDate() - 7);
    expectSameInstant(arg, expected);
  });
});

describe('mobile view', () => {
  it('renders MobileAgendaView when isMobile is true', () => {
    mockIsMobile = true;
    render(<MarketingCalendarPage />);
    expect(screen.getByTestId('mobile-agenda-view')).toBeInTheDocument();
  });

  it('does not render the desktop calendar when isMobile is true', () => {
    mockIsMobile = true;
    render(<MarketingCalendarPage />);
    expect(screen.queryByTestId('marketing-month-calendar')).not.toBeInTheDocument();
  });

  it('renders the desktop calendar when isMobile is false', () => {
    mockIsMobile = false;
    render(<MarketingCalendarPage />);
    expect(screen.queryByTestId('mobile-agenda-view')).not.toBeInTheDocument();
  });
});
