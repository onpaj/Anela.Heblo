import React from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import PhotobankBulkActionBar from "../PhotobankBulkActionBar";
import type { TagWithCountDto } from "../../../../api/hooks/usePhotobank";

const EXISTING_TAGS: TagWithCountDto[] = [
  { id: 1, name: "produkty", count: 5 },
  { id: 2, name: "promo", count: 3 },
];

const DEFAULT_PROPS = {
  selectedCount: 3,
  existingTags: EXISTING_TAGS,
  isApplying: false,
  onApplyTag: jest.fn().mockResolvedValue(undefined),
  onClear: jest.fn(),
};

function renderBar(overrides: Partial<typeof DEFAULT_PROPS> = {}) {
  return render(<PhotobankBulkActionBar {...DEFAULT_PROPS} {...overrides} />);
}

beforeEach(() => {
  (DEFAULT_PROPS.onApplyTag as jest.Mock).mockClear().mockResolvedValue(undefined);
  (DEFAULT_PROPS.onClear as jest.Mock).mockClear();
});

describe("PhotobankBulkActionBar", () => {
  test("renders selectedCount as '3 fotek vybráno'", () => {
    // Arrange & Act
    renderBar();

    // Assert
    expect(screen.getByText("3 fotek vybráno")).toBeInTheDocument();
  });

  test("input is empty initially and apply button is disabled", () => {
    // Arrange & Act
    renderBar();

    // Assert
    expect(screen.getByTestId("bulk-tag-input")).toHaveValue("");
    expect(screen.getByTestId("bulk-apply-btn")).toBeDisabled();
  });

  test("apply button enabled after typing a tag name", () => {
    // Arrange
    renderBar();

    // Act
    fireEvent.change(screen.getByTestId("bulk-tag-input"), {
      target: { value: "akce" },
    });

    // Assert
    expect(screen.getByTestId("bulk-apply-btn")).not.toBeDisabled();
  });

  test("clicking apply calls onApplyTag with trimmed value", async () => {
    // Arrange
    const onApplyTag = jest.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderBar({ onApplyTag });

    // Act
    await user.clear(screen.getByTestId("bulk-tag-input"));
    await user.type(screen.getByTestId("bulk-tag-input"), "  akce  ");
    await user.click(screen.getByTestId("bulk-apply-btn"));

    // Assert
    await waitFor(() => {
      expect(onApplyTag).toHaveBeenCalledWith("akce");
    });
  });

  test("pressing Enter in input calls onApplyTag", async () => {
    // Arrange
    const onApplyTag = jest.fn().mockResolvedValue(undefined);
    const user = userEvent.setup();
    renderBar({ onApplyTag });

    // Act
    await user.click(screen.getByTestId("bulk-tag-input"));
    await user.type(screen.getByTestId("bulk-tag-input"), "test");
    await user.keyboard("{Enter}");

    // Assert
    await waitFor(() => {
      expect(onApplyTag).toHaveBeenCalledWith("test");
    });
  });

  test("isApplying=true disables apply button", () => {
    // Arrange
    renderBar({ isApplying: true });

    // Act
    fireEvent.change(screen.getByTestId("bulk-tag-input"), {
      target: { value: "akce" },
    });

    // Assert
    expect(screen.getByTestId("bulk-apply-btn")).toBeDisabled();
  });

  test("selectedCount > 5000 disables apply button and shows hint", () => {
    // Arrange & Act
    renderBar({ selectedCount: 5001 });
    fireEvent.change(screen.getByTestId("bulk-tag-input"), {
      target: { value: "akce" },
    });

    // Assert
    expect(screen.getByTestId("bulk-apply-btn")).toBeDisabled();
    expect(screen.getByText(/Vyberte max\. 5 000 fotek/)).toBeInTheDocument();
  });

  test("selectedCount = 5000 does NOT disable apply button with limit hint", () => {
    // Arrange & Act
    renderBar({ selectedCount: 5000 });
    fireEvent.change(screen.getByTestId("bulk-tag-input"), {
      target: { value: "akce" },
    });

    // Assert
    expect(screen.getByTestId("bulk-apply-btn")).not.toBeDisabled();
    expect(screen.queryByText(/Vyberte max\. 5 000 fotek/)).not.toBeInTheDocument();
  });

  test("clicking clear calls onClear", () => {
    // Arrange
    const onClear = jest.fn();
    renderBar({ onClear });

    // Act
    fireEvent.click(screen.getByTestId("bulk-clear-btn"));

    // Assert
    expect(onClear).toHaveBeenCalledTimes(1);
  });

  test("input is cleared after successful apply", async () => {
    // Arrange
    renderBar();

    // Act
    fireEvent.change(screen.getByTestId("bulk-tag-input"), {
      target: { value: "akce" },
    });
    fireEvent.click(screen.getByTestId("bulk-apply-btn"));

    // Assert
    await waitFor(() => {
      expect(screen.getByTestId("bulk-tag-input")).toHaveValue("");
    });
  });

  test("shows inline error when onApplyTag throws", async () => {
    // Arrange
    const onApplyTag = jest.fn().mockRejectedValue(new Error("Network error"));
    renderBar({ onApplyTag });

    // Act
    fireEvent.change(screen.getByTestId("bulk-tag-input"), {
      target: { value: "akce" },
    });
    fireEvent.click(screen.getByTestId("bulk-apply-btn"));

    // Assert
    await waitFor(() => {
      expect(screen.getByText("Operace selhala. Zkuste to prosím znovu.")).toBeInTheDocument();
    });
    // Input should NOT be cleared on error
    expect(screen.getByTestId("bulk-tag-input")).toHaveValue("akce");
  });

  test("renders data-testid on bulk-action-bar container", () => {
    // Arrange & Act
    renderBar();

    // Assert
    expect(screen.getByTestId("bulk-action-bar")).toBeInTheDocument();
  });
});
