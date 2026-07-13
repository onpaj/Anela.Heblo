import React from "react";
import { render, screen } from "@testing-library/react";
import { formatDuration, getTimeUntilNextRun, getStatusBadge } from "../backgroundTasksHelpers";
import { RefreshTaskDto, RefreshTaskExecutionLogDto } from "../../api/generated/api-client";

describe("formatDuration", () => {
  it('formats "1.05:30:00" as "1d 5h"', () => {
    expect(formatDuration("1.05:30:00")).toBe("1d 5h");
  });

  it('formats "00:30:00" as "30m"', () => {
    expect(formatDuration("00:30:00")).toBe("30m");
  });

  it('formats "02:15:00" as "2h 15m"', () => {
    expect(formatDuration("02:15:00")).toBe("2h 15m");
  });

  it('formats "00:00:00" as "0m"', () => {
    expect(formatDuration("00:00:00")).toBe("0m");
  });

  it('formats "23:59:00" as "23h 59m"', () => {
    expect(formatDuration("23:59:00")).toBe("23h 59m");
  });

  it('formats "2.00:00:00" as "2d 0h"', () => {
    expect(formatDuration("2.00:00:00")).toBe("2d 0h");
  });
});

describe("getTimeUntilNextRun", () => {
  const NOW = new Date("2026-01-01T12:00:00.000Z");

  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(NOW);
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('returns "Spouští se..." for a timestamp before now', () => {
    const past = new Date(NOW.getTime() - 60 * 1000);
    expect(getTimeUntilNextRun(past)).toBe("Spouští se...");
  });

  it('returns "za 30 min" for a timestamp ~30 minutes from now', () => {
    const nextRun = new Date(NOW.getTime() + 30 * 60 * 1000);
    expect(getTimeUntilNextRun(nextRun)).toBe("za 30 min");
  });

  it('returns "za 1h 30m" for a timestamp ~90 minutes from now', () => {
    const nextRun = new Date(NOW.getTime() + 90 * 60 * 1000);
    expect(getTimeUntilNextRun(nextRun)).toBe("za 1h 30m");
  });

  it('returns "za 1d 5h" for a timestamp ~29 hours from now', () => {
    const nextRun = new Date(NOW.getTime() + 29 * 60 * 60 * 1000);
    expect(getTimeUntilNextRun(nextRun)).toBe("za 1d 5h");
  });

  it('returns "N/A" for undefined', () => {
    expect(getTimeUntilNextRun(undefined)).toBe("N/A");
  });

  it('returns "N/A" for null', () => {
    expect(getTimeUntilNextRun(null)).toBe("N/A");
  });

  it("handles a string-typed ISO date input the same as the equivalent Date object", () => {
    const nextRun = new Date(NOW.getTime() + 90 * 60 * 1000);
    expect(getTimeUntilNextRun(nextRun.toISOString())).toBe("za 1h 30m");
  });
});

describe("getStatusBadge", () => {
  it("renders 'Vypnuto' when task is disabled", () => {
    const task = new RefreshTaskDto({ enabled: false });
    render(<>{getStatusBadge(task)}</>);
    expect(screen.getByText("Vypnuto")).toBeInTheDocument();
  });

  it("renders 'Čeká' when task is enabled with no lastExecution", () => {
    const task = new RefreshTaskDto({ enabled: true });
    render(<>{getStatusBadge(task)}</>);
    expect(screen.getByText("Čeká")).toBeInTheDocument();
  });

  it("renders 'Běží' when lastExecution status is Running", () => {
    const task = new RefreshTaskDto({
      enabled: true,
      lastExecution: new RefreshTaskExecutionLogDto({ status: "Running" }),
    });
    render(<>{getStatusBadge(task)}</>);
    expect(screen.getByText("Běží")).toBeInTheDocument();
  });

  it("renders 'Úspěch' when lastExecution status is Completed", () => {
    const task = new RefreshTaskDto({
      enabled: true,
      lastExecution: new RefreshTaskExecutionLogDto({ status: "Completed" }),
    });
    render(<>{getStatusBadge(task)}</>);
    expect(screen.getByText("Úspěch")).toBeInTheDocument();
  });

  it("renders 'Chyba' when lastExecution status is Failed", () => {
    const task = new RefreshTaskDto({
      enabled: true,
      lastExecution: new RefreshTaskExecutionLogDto({ status: "Failed" }),
    });
    render(<>{getStatusBadge(task)}</>);
    expect(screen.getByText("Chyba")).toBeInTheDocument();
  });

  it("renders 'Zrušeno' when lastExecution status is Cancelled", () => {
    const task = new RefreshTaskDto({
      enabled: true,
      lastExecution: new RefreshTaskExecutionLogDto({ status: "Cancelled" }),
    });
    render(<>{getStatusBadge(task)}</>);
    expect(screen.getByText("Zrušeno")).toBeInTheDocument();
  });

  it("renders nothing for an unrecognized status", () => {
    const task = new RefreshTaskDto({
      enabled: true,
      lastExecution: new RefreshTaskExecutionLogDto({ status: "SomeUnknownStatus" }),
    });
    const { container } = render(<>{getStatusBadge(task)}</>);
    expect(container).toBeEmptyDOMElement();
  });
});
