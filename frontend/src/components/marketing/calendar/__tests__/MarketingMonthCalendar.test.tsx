import React from "react";
import { render } from "@testing-library/react";
import MarketingMonthCalendar from "../MarketingMonthCalendar";

// Capture the props passed to FullCalendar so each test can assert on them.
const fullCalendarPropsRef = {
  current: null,
};

jest.mock("@fullcalendar/react", () => {
  const React = require("react");
  const FullCalendarMock = React.forwardRef((props, _ref) => {
    fullCalendarPropsRef.current = props;
    return React.createElement("div", { "data-testid": "fullcalendar-mock" });
  });
  FullCalendarMock.displayName = "FullCalendarMock";
  return { __esModule: true, default: FullCalendarMock };
});

// Plugins are imported by the SUT but never invoked under the mock.
jest.mock("@fullcalendar/daygrid", () => ({ __esModule: true, default: {} }));
jest.mock("@fullcalendar/interaction", () => ({
  __esModule: true,
  default: {},
}));
jest.mock("@fullcalendar/core/locales/cs", () => ({
  __esModule: true,
  default: {},
}));

const noop = () => undefined;

const baseProps = {
  events: [],
  initialDate: new Date("2026-05-06T00:00:00"),
  onEventClick: noop,
  onEventMove: noop,
  onEventResize: noop,
  onDateRangeSelect: noop,
  onDatesSet: noop,
  calendarRef: React.createRef(),
};

beforeEach(() => {
  fullCalendarPropsRef.current = null;
});

describe("MarketingMonthCalendar — viewName prop", () => {
  it("passes viewName='fiveWeeks' as initialView to FullCalendar", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "fiveWeeks",
      }),
    );
    expect(fullCalendarPropsRef.current?.initialView).toBe("fiveWeeks");
  });
});
