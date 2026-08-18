import React from "react";
import { render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import LotLabelPrintModal, {
  defaultLotNumber,
  formatExpiration,
  CALIBRATION_READ_PERMISSION,
  CALIBRATION_WRITE_PERMISSION,
} from "../LotLabelPrintModal";
import * as useMaterialContainersHooks from "../../../api/hooks/useMaterialContainers";
import * as permissionsContext from "../../../auth/PermissionsContext";
import { ACCESS_ROLES } from "../../../auth/accessMatrix.generated";

jest.mock("../../../api/hooks/useMaterialContainers");
jest.mock("../../../auth/PermissionsContext");

const mockHooks = useMaterialContainersHooks as jest.Mocked<
  typeof useMaterialContainersHooks
>;
const mockPermissions = permissionsContext as jest.Mocked<
  typeof permissionsContext
>;

// Grants an explicit set of permission strings. The component's literals must match
// exactly, so an agnostic `() => true` mock would hide a typo in them.
const setGrantedPermissions = (granted: string[]) => {
  (mockPermissions.usePermissionsContext as jest.Mock) = jest
    .fn()
    .mockReturnValue({
      hasPermission: (permission: string) => granted.includes(permission),
    });
};

const setPermission = (granted: boolean) =>
  setGrantedPermissions(
    granted ? [CALIBRATION_READ_PERMISSION, CALIBRATION_WRITE_PERMISSION] : [],
  );

describe("calibration permission literals", () => {
  // A typo here cannot fail any other test — the gate would just evaluate to false and
  // the form would silently disappear for everyone. Pin them to the generated matrix.
  it.each([CALIBRATION_READ_PERMISSION, CALIBRATION_WRITE_PERMISSION])(
    "%s exists in the generated access matrix",
    (permission) => {
      expect(ACCESS_ROLES).toContain(permission);
    },
  );
});

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
      data: {
        pitchDots: 148,
        minPitchDots: 80,
        maxPitchDots: 400,
        driftDotsPer100Labels: 30,
        minDriftDotsPer100Labels: 0,
        maxDriftDotsPer100Labels: 1000,
      },
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

  it("prefills lot number, expiration and count from predefined props", () => {
    render(
      <LotLabelPrintModal
        isOpen={true}
        onClose={jest.fn()}
        initialLotNumber="2926"
        initialExpirationMonth="2029-07"
        initialCount={42}
      />,
    );
    const lotInput = screen.getByLabelText(/Číslo šarže/i) as HTMLInputElement;
    const expirationInput = screen.getByLabelText(/Expirace/i) as HTMLInputElement;
    const countInput = screen.getByLabelText(/Počet štítků/i) as HTMLInputElement;
    expect(lotInput.value).toBe("2926");
    expect(expirationInput.value).toBe("2029-07");
    expect(countInput.value).toBe("42");
    expect(screen.getByText(/Na štítku: 07\/29/i)).toBeInTheDocument();
  });

  it("clamps a predefined count above the maximum", () => {
    render(
      <LotLabelPrintModal isOpen={true} onClose={jest.fn()} initialCount={5000} />,
    );
    const countInput = screen.getByLabelText(/Počet štítků/i) as HTMLInputElement;
    expect(countInput.value).toBe("200");
  });

  it("defaults the count to 1 when the predefined count is zero", () => {
    render(
      <LotLabelPrintModal isOpen={true} onClose={jest.fn()} initialCount={0} />,
    );
    const countInput = screen.getByLabelText(/Počet štítků/i) as HTMLInputElement;
    expect(countInput.value).toBe("1");
  });

  it("falls back to the ISO-week default when the predefined lot number is empty", () => {
    render(
      <LotLabelPrintModal
        isOpen={true}
        onClose={jest.fn()}
        initialLotNumber=""
        initialExpirationMonth=""
      />,
    );
    const lotInput = screen.getByLabelText(/Číslo šarže/i) as HTMLInputElement;
    const expirationInput = screen.getByLabelText(/Expirace/i) as HTMLInputElement;
    expect(lotInput.value).toBe(defaultLotNumber());
    expect(expirationInput.value).toBe("");
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
      { lotNumber: "2926", expiration: "07/29", count: 3, mediaChangeConfirmed: false },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );
  });

  it("keeps the print tab active by default and hides calibration controls", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

    expect(screen.getByLabelText(/Číslo šarže/i)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Zkušební kříž/i }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));

    expect(
      screen.getByRole("button", { name: /Zkušební kříž/i }),
    ).toBeInTheDocument();
  });

  it("prints a calibration cross without content and keeps the modal open", () => {
    const onClose = jest.fn();
    render(<LotLabelPrintModal isOpen={true} onClose={onClose} />);

    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
    fireEvent.click(screen.getByRole("button", { name: /Zkušební kříž/i }));

    expect(mockCalibrationMutate).toHaveBeenCalledWith(
      { mediaChangeConfirmed: false },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );
    // Calibration does not depend on lot content and does not print a real label.
    expect(mockMutate).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("keeps the calibration button enabled without an expiration", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
    expect(screen.getByRole("button", { name: /Zkušební kříž/i })).toBeEnabled();
  });

  it("feeds the media forward by 1, 3 and 5 steps", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));

    fireEvent.click(screen.getByRole("button", { name: /^\+1$/ }));
    expect(mockFeedMutate).toHaveBeenLastCalledWith(
      { dots: 4, mediaChangeConfirmed: false },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );

    fireEvent.click(screen.getByRole("button", { name: /^\+3$/ }));
    expect(mockFeedMutate).toHaveBeenLastCalledWith(
      { dots: 12, mediaChangeConfirmed: false },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );

    fireEvent.click(screen.getByRole("button", { name: /^\+5$/ }));
    expect(mockFeedMutate).toHaveBeenLastCalledWith(
      { dots: 20, mediaChangeConfirmed: false },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );
  });

  it("hides the pitch calibration field from non-admins", () => {
    setPermission(false);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
    expect(screen.queryByLabelText(/Rozteč štítků/i)).not.toBeInTheDocument();
  });

  it("shows pitch + drift fields for admins, prefilled, and saves both", () => {
    setPermission(true);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));

    const pitchInput = screen.getByLabelText(/Rozteč štítků/i) as HTMLInputElement;
    const driftInput = screen.getByLabelText(/Korekce driftu/i) as HTMLInputElement;
    expect(pitchInput.value).toBe("148");
    expect(driftInput.value).toBe("30");

    fireEvent.change(pitchInput, { target: { value: "152" } });
    fireEvent.change(driftInput, { target: { value: "40" } });
    fireEvent.click(screen.getByRole("button", { name: /Uložit kalibraci/i }));

    expect(mockSaveCalibration).toHaveBeenLastCalledWith(
      { pitchDots: 152, driftDotsPer100Labels: 40 },
      expect.objectContaining({ onError: expect.any(Function) }),
    );
  });

  it("hides the calibration fields when write is granted without read", () => {
    // The API needs Read to load the current values, so a write-only grant would
    // otherwise render an inert form that can never be saved.
    setGrantedPermissions([CALIBRATION_WRITE_PERMISSION]);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
    expect(screen.queryByLabelText(/Rozteč štítků/i)).not.toBeInTheDocument();
  });

  it("hides the calibration fields when read is granted without write", () => {
    setGrantedPermissions([CALIBRATION_READ_PERMISSION]);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
    expect(screen.queryByLabelText(/Rozteč štítků/i)).not.toBeInTheDocument();
  });

  it("only enables the calibration query once both permissions are held", () => {
    setGrantedPermissions([CALIBRATION_WRITE_PERMISSION]);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    expect(mockHooks.useLotLabelCalibration).toHaveBeenLastCalledWith(false);

    setPermission(true);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    expect(mockHooks.useLotLabelCalibration).toHaveBeenLastCalledWith(true);
  });

  it("surfaces a calibration load failure instead of showing an empty form", () => {
    setPermission(true);
    (mockHooks.useLotLabelCalibration as jest.Mock) = jest.fn().mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error("Forbidden"),
    });

    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));

    expect(screen.getByTestId("calibration-load-error")).toHaveTextContent(
      "Forbidden",
    );
  });

  it("stays open after a successful print", () => {
    const onClose = jest.fn();
    mockMutate.mockImplementation((_input, opts) => opts?.onSuccess?.());

    render(<LotLabelPrintModal isOpen={true} onClose={onClose} />);

    fireEvent.change(screen.getByLabelText(/Expirace/i), {
      target: { value: "2029-07" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Vytisknout/i }));

    expect(mockMutate).toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("shows the media-change dialog when a print is blocked, then reprints confirmed on approval", () => {
    // The backend blocks the first (unconfirmed) print, then allows the confirmed retry.
    mockMutate.mockImplementation((input, opts) =>
      opts?.onSuccess?.({ requiresMediaChangeConfirmation: !input.mediaChangeConfirmed }),
    );

    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.change(screen.getByLabelText(/Expirace/i), {
      target: { value: "2029-07" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Vytisknout/i }));

    // First attempt was blocked -> dialog visible, print not yet confirmed.
    expect(screen.getByTestId("printer-media-change-dialog")).toBeInTheDocument();
    expect(mockMutate).toHaveBeenCalledTimes(1);
    expect(mockMutate.mock.calls[0][0].mediaChangeConfirmed).toBe(false);

    fireEvent.click(screen.getByRole("button", { name: /Pokračovat v tisku/i }));

    // Confirmed retry fired and the dialog closed.
    expect(mockMutate).toHaveBeenCalledTimes(2);
    expect(mockMutate.mock.calls[1][0].mediaChangeConfirmed).toBe(true);
    expect(
      screen.queryByTestId("printer-media-change-dialog"),
    ).not.toBeInTheDocument();
  });

  it("closes the media-change dialog without reprinting when cancelled", () => {
    mockMutate.mockImplementation((input, opts) =>
      opts?.onSuccess?.({ requiresMediaChangeConfirmation: !input.mediaChangeConfirmed }),
    );

    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.change(screen.getByLabelText(/Expirace/i), {
      target: { value: "2029-07" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Vytisknout/i }));

    const dialog = screen.getByTestId("printer-media-change-dialog");
    expect(dialog).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: /Zrušit/i }));

    expect(
      screen.queryByTestId("printer-media-change-dialog"),
    ).not.toBeInTheDocument();
    expect(mockMutate).toHaveBeenCalledTimes(1);
  });
});
