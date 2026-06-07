import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import BulkTagDialog from "../BulkTagDialog";
import type { TagWithCountDto } from "../../../../api/hooks/usePhotobank";

// ---- Mocks ------------------------------------------------------------------

const mockShowSuccess = jest.fn();
const mockTrackEvent = jest.fn();

jest.mock("../../../../api/hooks/usePhotobank", () => ({
  useBulkAddPhotoTag: jest.fn(),
}));

jest.mock("../../../../contexts/ToastContext", () => ({
  useToast: () => ({ showSuccess: mockShowSuccess, showError: jest.fn() }),
}));

jest.mock("../../../../telemetry/useTelemetry", () => ({
  useTelemetry: () => ({ trackEvent: mockTrackEvent }),
}));

const { useBulkAddPhotoTag } = jest.requireMock("../../../../api/hooks/usePhotobank") as {
  useBulkAddPhotoTag: jest.Mock;
};

// ---- Helpers ----------------------------------------------------------------

const EXISTING_TAGS: TagWithCountDto[] = [
  { id: 1, name: "produkty", count: 10 },
  { id: 2, name: "promo", count: 5 },
  { id: 3, name: "portfolio", count: 3 },
];

const DEFAULT_PROPS = {
  search: "",
  selectedTagNames: [],
  totalMatching: 42,
  existingTags: EXISTING_TAGS,
  onClose: jest.fn(),
};

function buildMockMutation(overrides: Partial<{ mutateAsync: jest.Mock; isPending: boolean }> = {}) {
  return {
    mutateAsync: jest.fn().mockResolvedValue({ success: true, tagName: "test", addedCount: 5, alreadyTaggedCount: 0 }),
    isPending: false,
    ...overrides,
  };
}

function renderDialog(props: Partial<typeof DEFAULT_PROPS> = {}) {
  return render(<BulkTagDialog {...DEFAULT_PROPS} {...props} />);
}

// ---- Tests ------------------------------------------------------------------

beforeEach(() => {
  mockShowSuccess.mockClear();
  mockTrackEvent.mockClear();
  (DEFAULT_PROPS.onClose as jest.Mock).mockClear();
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation());
});

test("renders filter chips for active search filter", () => {
  renderDialog({ search: "produkty", selectedTagNames: [] });

  expect(screen.getByText('Název: "produkty"')).toBeInTheDocument();
});

test("renders filter chips for each selected tag name", () => {
  renderDialog({ search: "", selectedTagNames: ["jarní", "letní"] });

  expect(screen.getByText("jarní")).toBeInTheDocument();
  expect(screen.getByText("letní")).toBeInTheDocument();
});

test("submit button is disabled when tag name is empty", () => {
  renderDialog();

  const submitBtn = screen.getByRole("button", { name: /Použít/i });
  expect(submitBtn).toBeDisabled();
});

test("submit button is enabled after typing a tag name", () => {
  renderDialog();

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });

  const submitBtn = screen.getByRole("button", { name: /Použít/i });
  expect(submitBtn).not.toBeDisabled();
});

test("shows inline error on BulkTagLimitExceeded (errorCode 2606)", async () => {
  const mutateAsync = jest.fn().mockResolvedValue({
    success: false,
    errorCode: 2606,
    params: { Count: "6000", Limit: "5000" },
  });
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ search: "foo" });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(screen.getByText(/příliš mnoha fotkám \(6000\)/i)).toBeInTheDocument();
  });
  expect(screen.getByText(/max 5000/i)).toBeInTheDocument();
  expect(DEFAULT_PROPS.onClose).not.toHaveBeenCalled();
});

test("calls showSuccess and onClose after successful submission", async () => {
  const mutateAsync = jest.fn().mockResolvedValue({
    success: true,
    tagName: "test",
    addedCount: 5,
    alreadyTaggedCount: 0,
  });
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ search: "foo" });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "test" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(mockShowSuccess).toHaveBeenCalledWith(
      "Štítek přidán",
      'Přidán štítek "test" k 5 fotkám (0 už ho mělo).',
    );
  });
  expect(DEFAULT_PROPS.onClose).toHaveBeenCalled();
});

test("autocomplete dropdown appears when typing a matching prefix", () => {
  renderDialog();

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "pro" } });

  // "produkty" and "promo" both contain "pro"; "portfolio" contains "por", not "pro" — should not appear in suggestions
  expect(screen.getByText("produkty")).toBeInTheDocument();
  expect(screen.getByText("promo")).toBeInTheDocument();
  expect(screen.queryByText("portfolio")).not.toBeInTheDocument();
});

test("autocomplete dropdown is hidden when input is empty", () => {
  renderDialog();

  // Input starts empty — no suggestions rendered
  expect(screen.queryByText("produkty")).not.toBeInTheDocument();
});

test("clicking a suggestion fills the input", () => {
  renderDialog();

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "pro" } });
  fireEvent.mouseDown(screen.getByText("produkty"));

  expect(screen.getByLabelText("Štítek")).toHaveValue("produkty");
});

test("shows generic error message for unknown error codes", async () => {
  const mutateAsync = jest.fn().mockResolvedValue({
    success: false,
    errorCode: 9999,
  });
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ search: "foo" });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(screen.getByText("Operace selhala. Zkuste to prosím znovu.")).toBeInTheDocument();
  });
});

test("backdrop click calls onClose", () => {
  renderDialog();

  fireEvent.click(screen.getByTestId("bulk-tag-dialog-backdrop"));

  expect(DEFAULT_PROPS.onClose).toHaveBeenCalled();
});

test("submit button is disabled when isPending is true", () => {
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ isPending: true }));

  renderDialog();

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });

  const submitBtn = screen.getByRole("button", { name: /Použít/i });
  expect(submitBtn).toBeDisabled();
});

test("shows generic error message when mutateAsync rejects", async () => {
  const mutateAsync = jest.fn().mockRejectedValue(new Error("Network error"));
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ search: "foo" });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(screen.getByText("Operace selhala. Zkuste to prosím znovu.")).toBeInTheDocument();
  });
});

// ---- Telemetry tests --------------------------------------------------------

test("tracks PhotobankBulkTagApplied with tagCount and photoCount on successful submit", async () => {
  const mutateAsync = jest.fn().mockResolvedValue({
    success: true,
    tagName: "summer",
    addedCount: 3,
    alreadyTaggedCount: 0,
  });
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ selectedTagNames: [] });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "summer" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(mockTrackEvent).toHaveBeenCalledWith(
      "PhotobankBulkTagApplied",
      { tagCount: "1" },
      { photoCount: 3 },
    );
  });
});

test("tracks PhotobankBulkTagApplied with correct tagCount when selectedTagNames is provided", async () => {
  const mutateAsync = jest.fn().mockResolvedValue({
    success: true,
    tagName: "akce",
    addedCount: 7,
    alreadyTaggedCount: 2,
  });
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ selectedTagNames: ["jarní", "letní", "promo"] });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(mockTrackEvent).toHaveBeenCalledWith(
      "PhotobankBulkTagApplied",
      { tagCount: "3" },
      { photoCount: 7 },
    );
  });
});

test("does not track when submit returns an error", async () => {
  const mutateAsync = jest.fn().mockResolvedValue({
    success: false,
    errorCode: 2606,
    params: { Count: 6000, Limit: 5000 },
  });
  useBulkAddPhotoTag.mockReturnValue(buildMockMutation({ mutateAsync }));

  renderDialog({ search: "foo" });

  fireEvent.change(screen.getByLabelText("Štítek"), { target: { value: "akce" } });
  fireEvent.click(screen.getByRole("button", { name: /Použít/i }));

  await waitFor(() => {
    expect(mutateAsync).toHaveBeenCalled();
  });
  expect(mockTrackEvent).not.toHaveBeenCalled();
});
