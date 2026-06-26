import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ToastProvider } from "../../contexts/ToastContext";
import ExpeditionListArchivePage from "../ExpeditionListArchivePage";

jest.mock("../../api/hooks/useExpeditionListArchive", () => ({
  useExpeditionDates: jest.fn(),
  useExpeditionListsByDate: jest.fn(),
  useReprintExpeditionList: jest.fn(),
  getExpeditionListDownloadUrl: jest.fn(),
}));

jest.mock("../../api/hooks/useExpeditionList", () => ({
  useRunExpeditionListPrintFix: jest.fn(),
  usePrintExpeditionOrder: jest.fn(),
}));

jest.mock("../../api/hooks/useRecurringJobs", () => ({
  useTriggerRecurringJobMutation: jest.fn(),
  useRecurringJobQuery: jest.fn(),
  useUpdateRecurringJobStatusMutation: jest.fn(),
}));

jest.mock("../../auth/PermissionsContext", () => ({
  usePermissionsContext: jest.fn(),
}));

jest.mock("../../api/client", () => ({
  getAuthenticatedApiClient: jest.fn(),
  getAuthenticatedFetch: jest.fn(() => jest.fn()),
  QUERY_KEYS: {
    expeditionListArchive: ["expedition-list-archive"],
  },
}));

const {
  useExpeditionDates,
  useExpeditionListsByDate,
  useReprintExpeditionList,
} = require("../../api/hooks/useExpeditionListArchive");

const { useRunExpeditionListPrintFix, usePrintExpeditionOrder } = require("../../api/hooks/useExpeditionList");

const {
  useTriggerRecurringJobMutation,
  useRecurringJobQuery,
  useUpdateRecurringJobStatusMutation,
} = require("../../api/hooks/useRecurringJobs");

const { usePermissionsContext } = require("../../auth/PermissionsContext");

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

const mockDatesData = {
  data: { dates: ["2024-12-10", "2024-12-09"], totalCount: 2, page: 1, pageSize: 20 },
  isLoading: false,
};

const mockItemsData = {
  data: {
    items: [
      {
        blobPath: "blob/path/file.pdf",
        fileName: "expedice-2024-12-10.pdf",
        createdOn: "2024-12-10T10:00:00Z",
        contentLength: 1024,
      },
    ],
  },
  isLoading: false,
};

const createQueryClient = () =>
  new QueryClient({ defaultOptions: { queries: { retry: false } } });

const renderPage = (queryClient: QueryClient) =>
  render(
    <QueryClientProvider client={queryClient}>
      <ToastProvider>
        <ExpeditionListArchivePage />
      </ToastProvider>
    </QueryClientProvider>
  );

const setCommonMocks = () => {
  (useExpeditionDates as jest.Mock).mockReturnValue(mockDatesData);
  (useExpeditionListsByDate as jest.Mock).mockReturnValue(mockItemsData);
  (useReprintExpeditionList as jest.Mock).mockReturnValue({
    mutateAsync: jest.fn().mockResolvedValue({ success: true, errorCode: null, params: null }),
    isPending: false,
  });
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

describe("ExpeditionListArchivePage – refresh button", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  it("renders the refresh button", () => {
    renderPage(createQueryClient());
    expect(screen.getByRole("button", { name: /obnovit/i })).toBeInTheDocument();
  });

  it("invalidates expedition archive queries when refresh is clicked", async () => {
    const queryClient = createQueryClient();
    const spy = jest.spyOn(queryClient, "invalidateQueries").mockResolvedValue();

    renderPage(queryClient);

    fireEvent.click(screen.getByRole("button", { name: /obnovit/i }));

    await waitFor(() => {
      expect(spy).toHaveBeenCalledWith({ queryKey: ["expedition-list-archive"] });
    });
    // Wait for isRefreshing state to settle to avoid act() warnings
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /obnovit/i })).not.toBeDisabled()
    );
  });

  it("disables the refresh button while invalidation is in progress", async () => {
    const queryClient = createQueryClient();
    let resolveInvalidate!: () => void;
    jest
      .spyOn(queryClient, "invalidateQueries")
      .mockReturnValue(new Promise<void>((resolve) => { resolveInvalidate = resolve; }));

    renderPage(queryClient);

    const button = screen.getByRole("button", { name: /obnovit/i });
    fireEvent.click(button);

    expect(button).toBeDisabled();

    resolveInvalidate();
    await waitFor(() => expect(button).not.toBeDisabled());
  });

  it("re-enables the refresh button after invalidation completes", async () => {
    const queryClient = createQueryClient();
    jest.spyOn(queryClient, "invalidateQueries").mockResolvedValue();

    renderPage(queryClient);

    const button = screen.getByRole("button", { name: /obnovit/i });
    fireEvent.click(button);

    await waitFor(() => expect(button).not.toBeDisabled());
  });
});

describe("ExpeditionListArchivePage – expedition robot toggle", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  const getToggle = () =>
    screen.getByRole("switch", { name: /expediční robot/i });

  it("reflects the enabled state of the print job", () => {
    setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });

    renderPage(createQueryClient());

    expect(getToggle()).toHaveAttribute("aria-checked", "true");
  });

  it("reflects the disabled state of the print job", () => {
    setPrintJob({ jobName: "print-picking-list", isEnabled: false, nextRunAt: null });

    renderPage(createQueryClient());

    expect(getToggle()).toHaveAttribute("aria-checked", "false");
  });

  it("calls the status mutation with the negated value when toggled", async () => {
    const mutateAsync = jest.fn().mockResolvedValue(undefined);
    setPrintJob({ jobName: "print-picking-list", isEnabled: true, nextRunAt: new Date("2024-12-11T08:00:00Z") });
    (useUpdateRecurringJobStatusMutation as jest.Mock).mockReturnValue({
      mutateAsync,
      isPending: false,
    });

    renderPage(createQueryClient());

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

    renderPage(createQueryClient());

    expect(screen.getByText(/Další běh: –/)).toBeInTheDocument();
    expect(getToggle()).toBeDisabled();
  });
});

describe("ExpeditionListArchivePage – permission gating", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    setCommonMocks();
  });

  it("shows the run button only with the trigger permission", () => {
    setPermissions([TRIGGER_PERMISSION]);
    renderPage(createQueryClient());
    expect(screen.getByRole("button", { name: /spustit tisk$/i })).toBeInTheDocument();
  });

  it("hides the run button without the trigger permission", () => {
    setPermissions([DISABLE_PERMISSION]);
    renderPage(createQueryClient());
    expect(screen.queryByRole("button", { name: /spustit tisk$/i })).not.toBeInTheDocument();
  });

  it("shows the toggle only with the disable permission", () => {
    setPermissions([DISABLE_PERMISSION]);
    renderPage(createQueryClient());
    expect(screen.getByRole("switch", { name: /expediční robot/i })).toBeInTheDocument();
  });

  it("hides the toggle without the disable permission", () => {
    setPermissions([TRIGGER_PERMISSION]);
    renderPage(createQueryClient());
    expect(screen.queryByRole("switch", { name: /expediční robot/i })).not.toBeInTheDocument();
  });

  it("hides the toggle and next-run entirely with neither job permission", () => {
    setPermissions([]);
    renderPage(createQueryClient());
    expect(screen.queryByRole("switch", { name: /expediční robot/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/Další běh:/)).not.toBeInTheDocument();
  });
});
