import React from "react";
import { renderHook, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { draftsEqual, useUnsavedChangesDialog } from "../useUnsavedChangesDialog";

const mockNavigate = jest.fn();

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <MemoryRouter>{children}</MemoryRouter>
);

beforeEach(() => mockNavigate.mockClear());

describe("draftsEqual", () => {
  it("treats array fields as order-insensitive", () => {
    expect(draftsEqual({ ids: ["a", "b"] }, { ids: ["b", "a"] })).toBe(true);
  });

  it("detects scalar field differences", () => {
    expect(draftsEqual({ name: "x" }, { name: "y" })).toBe(false);
  });

  it("detects array membership differences", () => {
    expect(draftsEqual({ ids: ["a"] }, { ids: ["a", "b"] })).toBe(false);
  });

  it("returns false when one side is null", () => {
    expect(draftsEqual(null, { name: "x" })).toBe(false);
  });
});

describe("useUnsavedChangesDialog", () => {
  it("navigates immediately when not dirty", () => {
    const save = jest.fn().mockResolvedValue(true);
    const { result } = renderHook(() => useUnsavedChangesDialog(false, save), { wrapper });

    act(() => result.current.requestNavigation("/dest"));

    expect(mockNavigate).toHaveBeenCalledWith("/dest");
    expect(result.current.dialogProps.isOpen).toBe(false);
  });

  it("opens the dialog instead of navigating when dirty", () => {
    const save = jest.fn().mockResolvedValue(true);
    const { result } = renderHook(() => useUnsavedChangesDialog(true, save), { wrapper });

    act(() => result.current.requestNavigation("/dest"));

    expect(mockNavigate).not.toHaveBeenCalled();
    expect(result.current.dialogProps.isOpen).toBe(true);
  });

  it("Discard navigates to the pending destination", () => {
    const save = jest.fn().mockResolvedValue(true);
    const { result } = renderHook(() => useUnsavedChangesDialog(true, save), { wrapper });

    act(() => result.current.requestNavigation("/dest"));
    act(() => result.current.dialogProps.onDiscard());

    expect(mockNavigate).toHaveBeenCalledWith("/dest");
    expect(result.current.dialogProps.isOpen).toBe(false);
  });

  it("Keep editing closes the dialog without navigating", () => {
    const save = jest.fn().mockResolvedValue(true);
    const { result } = renderHook(() => useUnsavedChangesDialog(true, save), { wrapper });

    act(() => result.current.requestNavigation("/dest"));
    act(() => result.current.dialogProps.onKeepEditing());

    expect(mockNavigate).not.toHaveBeenCalled();
    expect(result.current.dialogProps.isOpen).toBe(false);
  });

  it("Save navigates only when save resolves true", async () => {
    const save = jest.fn().mockResolvedValue(true);
    const { result } = renderHook(() => useUnsavedChangesDialog(true, save), { wrapper });

    act(() => result.current.requestNavigation("/dest"));
    await act(async () => {
      await result.current.dialogProps.onSave();
    });

    expect(save).toHaveBeenCalled();
    expect(mockNavigate).toHaveBeenCalledWith("/dest");
    expect(result.current.dialogProps.isOpen).toBe(false);
  });

  it("Save keeps the dialog open when save resolves false", async () => {
    const save = jest.fn().mockResolvedValue(false);
    const { result } = renderHook(() => useUnsavedChangesDialog(true, save), { wrapper });

    act(() => result.current.requestNavigation("/dest"));
    await act(async () => {
      await result.current.dialogProps.onSave();
    });

    expect(save).toHaveBeenCalled();
    expect(mockNavigate).not.toHaveBeenCalled();
    expect(result.current.dialogProps.isOpen).toBe(true);
  });
});
