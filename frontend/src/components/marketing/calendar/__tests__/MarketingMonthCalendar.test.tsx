import React from "react";
import { render, screen } from "@testing-library/react";
import MarketingMonthCalendar from "../MarketingMonthCalendar";

// Capture the props passed to FullCalendar so each test can assert on them.
const fullCalendarPropsRef = {
  current: null as any,
};

jest.mock("@fullcalendar/react", () => {
  const React = require("react");
  const FullCalendarMock = React.forwardRef((props: any, _ref: any) => {
    fullCalendarPropsRef.current = props;
    return React.createElement("div", { "data-testid": "fullcalendar-mock" });
  });
  FullCalendarMock.displayName = "FullCalendarMock";
  return { __esModule: true, default: FullCalendarMock };
});

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
  calendarRef: React.createRef<any>(),
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

  it("passes viewName='twoWeeks' as initialView to FullCalendar", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "twoWeeks",
      }),
    );
    expect(fullCalendarPropsRef.current?.initialView).toBe("twoWeeks");
  });
});

describe("MarketingMonthCalendar — views registry", () => {
  it("registers both fiveWeeks and twoWeeks with correct dayMaxEvents", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "fiveWeeks",
      }),
    );
    const views = fullCalendarPropsRef.current?.views;
    expect(views).toBeDefined();
    expect(views.fiveWeeks).toEqual({
      type: "dayGrid",
      duration: { weeks: 5 },
      dayMaxEvents: true,
    });
    expect(views.twoWeeks).toEqual({
      type: "dayGrid",
      duration: { weeks: 2 },
      dayMaxEvents: false,
    });
  });

  it("does not pass a top-level dayMaxEvents prop", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "fiveWeeks",
      }),
    );
    expect(fullCalendarPropsRef.current).not.toHaveProperty("dayMaxEvents");
  });
});

describe("MarketingMonthCalendar — height and wrapper class", () => {
  it("uses height='100%' for fiveWeeks", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "fiveWeeks",
      }),
    );
    expect(fullCalendarPropsRef.current?.height).toBe("100%");
  });

  it("uses height='100%' for twoWeeks (fills viewport, taller rows)", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "twoWeeks",
      }),
    );
    expect(fullCalendarPropsRef.current?.height).toBe("100%");
  });

  it("does not add 'two-weeks' class to the wrapper for fiveWeeks", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "fiveWeeks",
      }),
    );
    const wrapper = screen.getByTestId("marketing-calendar-wrapper");
    expect(wrapper).toHaveClass("marketing-calendar");
    expect(wrapper).not.toHaveClass("two-weeks");
  });

  it("adds 'two-weeks' class to the wrapper for twoWeeks", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "twoWeeks",
      }),
    );
    const wrapper = screen.getByTestId("marketing-calendar-wrapper");
    expect(wrapper).toHaveClass("marketing-calendar");
    expect(wrapper).toHaveClass("two-weeks");
  });

  it("appends caller-provided className after the view class", () => {
    render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "twoWeeks",
        className: "extra-class",
      }),
    );
    const wrapper = screen.getByTestId("marketing-calendar-wrapper");
    expect(wrapper).toHaveClass("marketing-calendar", "two-weeks", "h-full", "extra-class");
  });
});

describe("MarketingMonthCalendar — eventContent", () => {
  it("uses a different eventContent function for twoWeeks vs fiveWeeks", () => {
    const { rerender } = render(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "fiveWeeks",
      }),
    );
    const compactContent = fullCalendarPropsRef.current?.eventContent;

    rerender(
      React.createElement(MarketingMonthCalendar, {
        ...baseProps,
        viewName: "twoWeeks",
      }),
    );
    const cardContent = fullCalendarPropsRef.current?.eventContent;

    expect(typeof compactContent).toBe("function");
    expect(typeof cardContent).toBe("function");
    expect(compactContent).not.toBe(cardContent);
  });
});
