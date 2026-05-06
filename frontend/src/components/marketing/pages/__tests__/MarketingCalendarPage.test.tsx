import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import MarketingCalendarPage from "../MarketingCalendarPage";

// Track every render of the calendar mock so tests can verify mount/unmount.
const calendarRenderLog: { viewName: string; mountId: number }[] = [];

jest.mock("../../calendar/MarketingMonthCalendar", () => {
  const React = require("react");
  let calendarMountCounter = 0;
  function MarketingMonthCalendarMock(props: { viewName: string }) {
    const mountId = React.useMemo(() => ++calendarMountCounter, []);
    React.useEffect(() => {
      calendarRenderLog.push({ viewName: props.viewName, mountId });
    });
    return (
      <div
        data-testid="marketing-month-calendar"
        data-view-name={props.viewName}
        data-mount-id={String(mountId)}
      />
    );
  }
  return { __esModule: true, default: MarketingMonthCalendarMock };
});

jest.mock("../../detail/MarketingActionModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../detail/ImportFromOutlookModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../list/MarketingActionGrid", () => ({
  __esModule: true,
  default: () => <div data-testid="marketing-action-grid" />,
}));

jest.mock("../../list/MarketingActionFilters", () => ({
  __esModule: true,
  default: () => <div data-testid="marketing-action-filters" />,
  EMPTY_FILTERS: { searchText: "", dateFrom: "", dateTo: "" },
}));

jest.mock("../../../manufacture/calendar/CalendarNavigation", () => ({
  __esModule: true,
  default: () => <div data-testid="calendar-navigation" />,
}));

// Capture every call to useMarketingCalendar so we can assert the fetch range.
const calendarHookCalls: { startDate: Date; endDate: Date }[] = [];

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useMarketingCalendar: (args: { startDate: Date; endDate: Date }) => {
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

beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
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

describe("MarketingCalendarPage — fallback fetch range", () => {
  it("requests a 35-day window for the default fiveWeeks view", () => {
    render(<MarketingCalendarPage />);
    expect(calendarHookCalls.length).toBeGreaterThan(0);
    const last = calendarHookCalls[calendarHookCalls.length - 1];
    expect(daysBetween(last.startDate, last.endDate)).toBe(35);
  });

  it("requests a 14-day window after switching to twoWeeks", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    const last = calendarHookCalls[calendarHookCalls.length - 1];
    expect(daysBetween(last.startDate, last.endDate)).toBe(14);
  });
});
