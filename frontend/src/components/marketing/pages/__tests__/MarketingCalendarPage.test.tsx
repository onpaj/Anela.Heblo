import React from "react";
import { render, screen } from "@testing-library/react";
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
