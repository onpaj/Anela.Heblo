import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
import LotLabelPrintModal, {
  defaultLotNumber,
  formatExpiration,
} from "../LotLabelPrintModal";
import * as useMaterialContainersHooks from "../../../api/hooks/useMaterialContainers";
import * as permissionsContext from "../../../auth/PermissionsContext";

jest.mock("../../../api/hooks/useMaterialContainers");
jest.mock("../../../auth/PermissionsContext");

const mockHooks = useMaterialContainersHooks as jest.Mocked<
  typeof useMaterialContainersHooks
>;
const mockPermissions = permissionsContext as jest.Mocked<
  typeof permissionsContext
>;

const setPermission = (granted: boolean) => {
  (mockPermissions.usePermissionsContext as jest.Mock) = jest
    .fn()
    .mockReturnValue({ hasPermission: () => granted });
};

describe("lot label helpers", () => {
  it("defaultLotNumber composes ISO week + 2-digit ISO week-year", () => {
    // 2026-07-15 is in ISO week 29 of 2026 -> "2926"
    expect(defaultLotNumber(new Date("2026-07-15T12:00:00Z"))).toBe("2926");
  });

  it("formatExpiration converts YYYY-MM to MM/YY", () => {
    expect(formatExpiration("2029-07")).toBe("07/29");
  });

  it("formatExpiration returns empty string for invalid input", () => {
    expect(formatExpiration("")).toBe("");
    expect(formatExpiration("2029")).toBe("");
  });
});

describe("LotLabelPrintModal", () => {
  const mockMutate = jest.fn();
  const mockCalibrationMutate = jest.fn();
  const mockFeedMutate = jest.fn();
  const mockSaveCalibration = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    setPermission(false);
    (mockHooks.usePrintLotLabels as jest.Mock) = jest.fn().mockReturnValue({
      mutate: mockMutate,
      isPending: false,
    });
    (mockHooks.usePrintLotCalibrationLabel as jest.Mock) = jest
      .fn()
      .mockReturnValue({
        mutate: mockCalibrationMutate,
        isPending: false,
      });
    (mockHooks.useFeedLotMedia as jest.Mock) = jest.fn().mockReturnValue({
      mutate: mockFeedMutate,
      isPending: false,
    });
    (mockHooks.useLotLabelCalibration as jest.Mock) = jest.fn().mockReturnValue({
      data: { pitchDots: 148, minPitchDots: 80, maxPitchDots: 400 },
      isLoading: false,
    });
    (mockHooks.useSetLotLabelCalibration as jest.Mock) = jest
      .fn()
      .mockReturnValue({ mutate: mockSaveCalibration, isPending: false });
  });

  it("renders nothing when closed", () => {
    render(<LotLabelPrintModal isOpen={false} onClose={jest.fn()} />);
    expect(screen.queryByTestId("lot-label-print-modal")).not.toBeInTheDocument();
  });

  it("prefills the lot number with the current ISO week + year", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    const lotInput = screen.getByLabelText(/Číslo šarže/i) as HTMLInputElement;
    expect(lotInput.value).toBe(defaultLotNumber());
  });

  it("disables print until an expiration is chosen", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    expect(screen.getByRole("button", { name: /Vytisknout/i })).toBeDisabled();
  });

  it("prints with lot number, MM/YY expiration and count", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

    fireEvent.change(screen.getByLabelText(/Číslo šarže/i), {
      target: { value: "2926" },
    });
    fireEvent.change(screen.getByLabelText(/Expirace/i), {
      target: { value: "2029-07" },
    });
    fireEvent.change(screen.getByLabelText(/Počet štítků/i), {
      target: { value: "3" },
    });

    fireEvent.click(screen.getByRole("button", { name: /Vytisknout 3/i }));

    expect(mockMutate).toHaveBeenCalledWith(
      { lotNumber: "2926", expiration: "07/29", count: 3 },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );
  });

  it("prints a calibration cross without content and keeps the modal open", () => {
    const onClose = jest.fn();
    render(<LotLabelPrintModal isOpen={true} onClose={onClose} />);

    fireEvent.click(screen.getByRole("button", { name: /Zkušební kříž/i }));

    expect(mockCalibrationMutate).toHaveBeenCalledWith(
      undefined,
      expect.objectContaining({ onError: expect.any(Function) }),
    );
    // Calibration does not depend on lot content and does not print a real label.
    expect(mockMutate).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("keeps the calibration button enabled without an expiration", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    expect(screen.getByRole("button", { name: /Zkušební kříž/i })).toBeEnabled();
  });

  it("feeds the media forward by 1, 3 and 5 steps", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: /^\+1$/ }));
    expect(mockFeedMutate).toHaveBeenLastCalledWith(
      4,
      expect.objectContaining({ onError: expect.any(Function) }),
    );

    fireEvent.click(screen.getByRole("button", { name: /^\+3$/ }));
    expect(mockFeedMutate).toHaveBeenLastCalledWith(
      12,
      expect.objectContaining({ onError: expect.any(Function) }),
    );

    fireEvent.click(screen.getByRole("button", { name: /^\+5$/ }));
    expect(mockFeedMutate).toHaveBeenLastCalledWith(
      20,
      expect.objectContaining({ onError: expect.any(Function) }),
    );
  });

  it("hides the pitch calibration field from non-admins", () => {
    setPermission(false);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    expect(screen.queryByLabelText(/Rozteč štítků/i)).not.toBeInTheDocument();
  });

  it("shows the pitch field for admins, prefilled, and saves it", () => {
    setPermission(true);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

    const pitchInput = screen.getByLabelText(/Rozteč štítků/i) as HTMLInputElement;
    expect(pitchInput.value).toBe("148");

    fireEvent.change(pitchInput, { target: { value: "152" } });
    fireEvent.click(screen.getByRole("button", { name: /Uložit rozteč/i }));

    expect(mockSaveCalibration).toHaveBeenLastCalledWith(
      152,
      expect.objectContaining({ onError: expect.any(Function) }),
    );
  });

  it("closes on successful print", async () => {
    const onClose = jest.fn();
    mockMutate.mockImplementation((_input, { onSuccess }) => onSuccess());

    render(<LotLabelPrintModal isOpen={true} onClose={onClose} />);

    fireEvent.change(screen.getByLabelText(/Expirace/i), {
      target: { value: "2029-07" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Vytisknout/i }));

    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });
});
