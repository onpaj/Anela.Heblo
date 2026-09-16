import React from "react";
import { render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import LotLabelPrintModal, {
  defaultLotNumber,
  formatExpiration,
} from "../LotLabelPrintModal";
import {
  CALIBRATION_READ_PERMISSION,
  CALIBRATION_WRITE_PERMISSION,
} from "../LotLabelCalibrationTab";
import * as useMaterialContainersHooks from "../../../api/hooks/useMaterialContainers";
import * as permissionsContext from "../../../auth/PermissionsContext";
import { ACCESS_ROLES } from "../../../auth/accessMatrix.generated";
import {
  LabelDriftDirection,
  LabelDriftSpeed,
} from "../../../api/generated/api-client";

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
  const mockNudgeCalibration = jest.fn();

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
    (mockHooks.useNudgeLotLabelCalibration as jest.Mock) = jest
      .fn()
      .mockReturnValue({ mutate: mockNudgeCalibration, isPending: false });
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

  it("puts the printer controls on the print tab, with no tab switch needed", () => {
    // Operators must reach media alignment and the drift buttons without hunting for a
    // second tab -- they never see one.
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

    expect(screen.getByLabelText(/Číslo šarže/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Zkušební kříž/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Nahoru rychle/i }),
    ).toBeInTheDocument();
  });

  it.each([true, false])(
    "offers the Kalibrace tab regardless of the calibration permission (granted=%s)",
    (granted) => {
      // The tab has never been permission-gated -- only the values inside it are. Hiding
      // it would make a missing grant indistinguishable from a broken screen.
      setPermission(granted);
      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      expect(
        screen.getByRole("button", { name: /Kalibrace/i }),
      ).toBeInTheDocument();
    },
  );

  it("explains the missing permission instead of showing a blank calibration tab", () => {
    setPermission(false);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));

    expect(screen.getByText(/nemáte oprávnění/i)).toBeInTheDocument();
    expect(screen.queryByLabelText(/Rozteč štítků/i)).not.toBeInTheDocument();
  });

  it("prints a calibration cross without content and keeps the modal open", () => {
    const onClose = jest.fn();
    render(<LotLabelPrintModal isOpen={true} onClose={onClose} />);

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
    expect(screen.getByRole("button", { name: /Zkušební kříž/i })).toBeEnabled();
  });

  it("feeds the media forward by 1, 3 and 5 steps", () => {
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

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

  it("keeps unsaved calibration edits when the query refetches", () => {
    // The query refetches on window focus; without a dirty guard the incoming value
    // would silently replace what the user typed.
    setPermission(true);
    const { rerender } = render(
      <LotLabelPrintModal isOpen={true} onClose={jest.fn()} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));

    const pitchInput = screen.getByLabelText(/Rozteč štítků/i) as HTMLInputElement;
    fireEvent.change(pitchInput, { target: { value: "152" } });

    // Someone else changed the calibration; a background refetch delivers the new value.
    (mockHooks.useLotLabelCalibration as jest.Mock).mockReturnValue({
      data: {
        pitchDots: 200,
        minPitchDots: 80,
        maxPitchDots: 400,
        driftDotsPer100Labels: 30,
        minDriftDotsPer100Labels: 0,
        maxDriftDotsPer100Labels: 1000,
      },
      isLoading: false,
    });
    rerender(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

    expect(
      (screen.getByLabelText(/Rozteč štítků/i) as HTMLInputElement).value,
    ).toBe("152");
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

  it("does not query the calibration at all from the print tab", () => {
    // The query lives in the calibration tab, which only mounts when it is selected.
    setPermission(true);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    expect(mockHooks.useLotLabelCalibration).not.toHaveBeenCalled();
  });

  it("leaves the calibration query disabled without both permissions", () => {
    // The tab opens for everyone, but the API needs the read permission, so the query
    // must stay disabled rather than firing a request that can only 403.
    setGrantedPermissions([CALIBRATION_WRITE_PERMISSION]);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
    expect(mockHooks.useLotLabelCalibration).toHaveBeenLastCalledWith(false);
  });

  it("enables the calibration query once both permissions are held", () => {
    setPermission(true);
    render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: /Kalibrace/i }));
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

  describe("drift wizard", () => {
    it("is available to operators without any calibration permission", () => {
      // The whole point of the wizard: the people who print the labels can correct the
      // drift themselves, unlike the raw pitch/drift fields.
      setPermission(false);
      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      expect(
        screen.getByRole("button", { name: /Nahoru rychle/i }),
      ).toBeInTheDocument();
      expect(screen.queryByLabelText(/Rozteč štítků/i)).not.toBeInTheDocument();
    });

    it.each([
      [/Nahoru rychle/i, LabelDriftDirection.Up, LabelDriftSpeed.Fast],
      [/Nahoru pomalu/i, LabelDriftDirection.Up, LabelDriftSpeed.Slow],
      [/Dolů rychle/i, LabelDriftDirection.Down, LabelDriftSpeed.Fast],
      [/Dolů pomalu/i, LabelDriftDirection.Down, LabelDriftSpeed.Slow],
    ])(
      "sends %s as direction/speed rather than a dot value",
      (buttonName, direction, speed) => {
        render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

        fireEvent.click(screen.getByRole("button", { name: buttonName }));

        expect(mockNudgeCalibration).toHaveBeenLastCalledWith(
          { direction, speed },
          expect.objectContaining({
            onSuccess: expect.any(Function),
            onError: expect.any(Function),
          }),
        );
      },
    );

    it("tells the operator to print another batch once the adjustment is saved", () => {
      mockNudgeCalibration.mockImplementation((_input, opts) =>
        opts?.onSuccess?.({ isAtLimit: false }),
      );
      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      expect(screen.queryByRole("status")).not.toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: /Nahoru rychle/i }));

      expect(screen.getByRole("status")).toHaveTextContent(/Vytiskněte/i);
    });

    it("says the range is exhausted rather than confirming an adjustment that never happened", () => {
      // Otherwise the operator prints another batch, sees the same drift and clicks
      // forever with nothing on screen admitting the server stopped moving.
      mockNudgeCalibration.mockImplementation((_input, opts) =>
        opts?.onSuccess?.({ isAtLimit: true }),
      );
      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: /Nahoru rychle/i }));

      expect(screen.getByRole("status")).toHaveTextContent(/na kraji rozsahu/i);
      expect(screen.queryByText(/Vytiskněte další dávku/i)).not.toBeInTheDocument();
    });

    it("surfaces a failed adjustment instead of silently doing nothing", () => {
      mockNudgeCalibration.mockImplementation((_input, opts) =>
        opts?.onError?.(new Error("Forbidden")),
      );
      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: /Dolů pomalu/i }));

      expect(screen.getByText(/Forbidden/)).toBeInTheDocument();
      expect(screen.queryByRole("status")).not.toBeInTheDocument();
    });

    it("clears a previous confirmation when a new adjustment is sent", () => {
      mockNudgeCalibration.mockImplementation((_input, opts) =>
        opts?.onSuccess?.({ isAtLimit: false }),
      );
      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      fireEvent.click(screen.getByRole("button", { name: /Nahoru rychle/i }));
      expect(screen.getByRole("status")).toBeInTheDocument();

      // A failing second attempt must not leave the earlier success message standing.
      mockNudgeCalibration.mockImplementation((_input, opts) =>
        opts?.onError?.(new Error("Boom")),
      );
      fireEvent.click(screen.getByRole("button", { name: /Nahoru pomalu/i }));

      expect(screen.queryByRole("status")).not.toBeInTheDocument();
      expect(screen.getByText(/Boom/)).toBeInTheDocument();
    });

    it("disables the wizard while an adjustment is in flight", () => {
      (mockHooks.useNudgeLotLabelCalibration as jest.Mock) = jest
        .fn()
        .mockReturnValue({ mutate: mockNudgeCalibration, isPending: true });

      render(<LotLabelPrintModal isOpen={true} onClose={jest.fn()} />);

      expect(screen.getByRole("button", { name: /Nahoru rychle/i })).toBeDisabled();
      expect(screen.getByRole("button", { name: /Dolů pomalu/i })).toBeDisabled();
    });
  });
});
