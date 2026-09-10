import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ToastProvider } from "../../../../contexts/ToastContext";
import ExpeditionJobControlsBar from "../ExpeditionJobControlsBar";

jest.mock("../../../../api/hooks/useExpeditionList", () => ({
  useRunExpeditionListPrintFix: jest.fn(),
  usePrintExpeditionOrder: jest.fn(),
}));

jest.mock("../../../../api/hooks/useRecurringJobs", () => ({
  useTriggerRecurringJobMutation: jest.fn(),
  useRecurringJobQuery: jest.fn(),
  useUpdateRecurringJobStatusMutation: jest.fn(),
}));

jest.mock("../../../../auth/PermissionsContext", () => ({
  usePermissionsContext: jest.fn(),
}));

jest.mock("../../../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: {
    expeditionListArchive: ["expedition-list-archive"],
  },
}));

const { useRunExpeditionListPrintFix, usePrintExpeditionOrder } = require("../../../../api/hooks/useExpeditionList");

const {
  useTriggerRecurringJobMutation,
  useRecurringJobQuery,
  useUpdateRecurringJobStatusMutation,
} = require("../../../../api/hooks/useRecurringJobs");

const { usePermissionsContext } = require("../../../../auth/PermissionsContext");

const TRIGGER_PERMISSION = "jobs.trigger.read";
const DISABLE_PERMISSION = "jobs.disable.read";

/** Sets the mocked permission context to grant exactly the listed permissions. */
const setPermissions = (granted: string[]) => {
  (usePermissionsContext as jest.Mock).mockReturnValue({
    hasPermission: (perm: string) => granted.includes(perm),
  });
};

const setPrintJob = (job: object | null) => {
  (useRecurringJobQuery as jest.Mock).mockReturnValue({ data: job });
};

const createQueryClient = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false } } });

const renderBar = (queryClient: QueryClient) =>
  render(
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <ExpeditionJobControlsBar />
      </ToastProvider>
    </QueryClientProvider>
  );

const setCommonMocks = () => {
  (useRunExpeditionListPrintFix as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ totalCount: 5 }),
    isPending: false,
  });
  (usePrintExpeditionOrder as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ success: true }),
    isPending: false,
  });
  (useTriggerRecurringJobMutation as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(undefined),
    isPending: false,
  });
  (useUpdateRecurringJobStatusMutation as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue(undefined),
    isPending: false,
  });
  setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });
  // Default: full permissions
  setPermissions([TRIGGER_PERMISSION, DISABLE_PERMISSION]);
};

describe("ExpeditionJobControlsBar – expedition robot toggle", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  const getToggle = () =>
    screen.getByRole("switch", { name: /expediční robot/i });

  it("reflects the enabled state of the print job", () => {
    setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });

    renderBar(createQueryClient());

    expect(getToggle()).toHaveAttribute("aria-checked", "true");
  });

  it("reflects the disabled state of the print job", () => {
    setPrintJob({ jobName: "print-picking-list", isEnabled: false, nextRunAt: null });

    renderBar(createQueryClient());

    expect(getToggle()).toHaveAttribute("aria-checked", "false");
  });

  it("calls the status mutation with the negated value when toggled", async () => {
    const mutateAsync = jest.fn().mockResolvedValue(undefined);
    setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });
    (useUpdateRecurringJobStatusMutation as jest.Mock).mockReturnValue({
      mutateAsync,
      isPending: false,
    });

    renderBar(createQueryClient());

    fireEvent.click(getToggle());

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        jobName: "print-picking-list",
        isEnabled: false,
      })
    );
  });

  it("renders an em dash for next run when the job is missing", () => {
    setPrintJob(null);

    renderBar(createQueryClient());

    expect(screen.getByText(/Další běh: –/)).toBeInTheDocument();
    expect(getToggle()).toBeDisabled();
  });
});

describe("ExpeditionJobControlsBar – permission gating", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  it("shows the run button only with the trigger permission", () => {
    setPermissions([TRIGGER_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.getByRole("button", { name: /spustit tisk$/i })).toBeInTheDocument();
  });

  it("hides the run button without the trigger permission", () => {
    setPermissions([DISABLE_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.queryByRole("button", { name: /spustit tisk$/i })).not.toBeInTheDocument();
  });

  it("shows the toggle only with the disable permission", () => {
    setPermissions([DISABLE_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.getByRole("switch", { name: /expediční robot/i })).toBeInTheDocument();
  });

  it("hides the toggle without the disable permission", () => {
    setPermissions([TRIGGER_PERMISSION]);
    renderBar(createQueryClient());
    expect(screen.queryByRole("switch", { name: /expediční robot/i })).not.toBeInTheDocument();
  });

  it("hides the toggle and next-run entirely with neither job permission", () => {
    setPermissions([]);
    renderBar(createQueryClient());
    expect(screen.queryByRole("switch", { name: /expediční robot/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/Další běh:/)).not.toBeInTheDocument();
  });
});
