import { ManufactureOrderState as S, ManufactureType as T } from "../../../../api/generated/api-client";
import { deriveStateTransitions, isStateTransitionBackward } from "../stateTransitions";

describe("deriveStateTransitions", () => {
  it.each([
    [S.Draft, [S.Planned, S.Cancelled], S.Planned, null],
    [S.Planned, [S.Draft, S.SemiProductManufactured, S.Completed, S.Cancelled], S.SemiProductManufactured, S.Draft],
    [S.SemiProductManufactured, [S.Planned, S.Completed, S.Cancelled], S.Completed, S.Planned],
    [S.Completed, [S.SemiProductManufactured, S.Planned, S.Cancelled], null, S.SemiProductManufactured],
  ])("MultiPhase %s", (current, available, next, previous) => {
    expect(deriveStateTransitions(current, available, T.MultiPhase)).toEqual({ next, previous });
  });

  it("SinglePhase Planned skips SemiProductManufactured", () => {
    expect(
      deriveStateTransitions(S.Planned, [S.Draft, S.SemiProductManufactured, S.Completed, S.Cancelled], T.SinglePhase)
    ).toEqual({ next: S.Completed, previous: S.Draft });
  });

  it("SinglePhase Completed goes back to Planned", () => {
    expect(deriveStateTransitions(S.Completed, [S.Planned, S.Cancelled], T.SinglePhase)).toEqual({
      next: null,
      previous: S.Planned,
    });
  });

  it("returns nulls for undefined or empty available", () => {
    expect(deriveStateTransitions(S.Planned, undefined, T.MultiPhase)).toEqual({ next: null, previous: null });
    expect(deriveStateTransitions(S.Planned, [], T.MultiPhase)).toEqual({ next: null, previous: null });
  });

  it("returns nulls for Cancelled", () => {
    expect(deriveStateTransitions(S.Cancelled, [S.Draft], T.MultiPhase)).toEqual({ next: null, previous: null });
  });
});

describe("isStateTransitionBackward", () => {
  it("detects direction", () => {
    expect(isStateTransitionBackward(S.Planned, S.Draft)).toBe(true);
    expect(isStateTransitionBackward(S.Draft, S.Planned)).toBe(false);
  });
});
