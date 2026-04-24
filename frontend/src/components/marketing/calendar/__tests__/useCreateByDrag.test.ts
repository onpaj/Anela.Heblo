import { renderHook, act } from "@testing-library/react";
import { useCreateByDrag } from "../useCreateByDrag";

describe("useCreateByDrag", () => {
  test("selection is null initially", () => {
    const onCreateRange = jest.fn();
    const { result } = renderHook(() => useCreateByDrag(onCreateRange));
    expect(result.current.selectionRange).toBeNull();
  });

  test("mouseDown + mouseEnter creates selection range", () => {
    const onCreateRange = jest.fn();
    const { result } = renderHook(() => useCreateByDrag(onCreateRange));

    act(() => result.current.handleMouseDown("2026-04-10"));
    act(() => result.current.handleMouseEnter("2026-04-14"));

    expect(result.current.selectionRange).toEqual({ from: "2026-04-10", to: "2026-04-14" });
  });

  test("selection sorts dates when dragging backwards", () => {
    const onCreateRange = jest.fn();
    const { result } = renderHook(() => useCreateByDrag(onCreateRange));

    act(() => result.current.handleMouseDown("2026-04-14"));
    act(() => result.current.handleMouseEnter("2026-04-10"));

    expect(result.current.selectionRange).toEqual({ from: "2026-04-10", to: "2026-04-14" });
  });

  test("mouseUp calls onCreateRange with sorted dates", () => {
    const onCreateRange = jest.fn();
    const { result } = renderHook(() => useCreateByDrag(onCreateRange));

    act(() => result.current.handleMouseDown("2026-04-10"));
    act(() => result.current.handleMouseEnter("2026-04-14"));
    act(() => result.current.handleMouseUp());

    expect(onCreateRange).toHaveBeenCalledWith("2026-04-10", "2026-04-14");
    expect(result.current.selectionRange).toBeNull();
  });

  test("mouseUp without prior mouseDown does nothing", () => {
    const onCreateRange = jest.fn();
    const { result } = renderHook(() => useCreateByDrag(onCreateRange));

    act(() => result.current.handleMouseUp());

    expect(onCreateRange).not.toHaveBeenCalled();
  });

  test("single-day click calls onCreateRange with same from and to", () => {
    const onCreateRange = jest.fn();
    const { result } = renderHook(() => useCreateByDrag(onCreateRange));

    act(() => result.current.handleMouseDown("2026-04-10"));
    act(() => result.current.handleMouseUp());

    expect(onCreateRange).toHaveBeenCalledWith("2026-04-10", "2026-04-10");
  });
});
