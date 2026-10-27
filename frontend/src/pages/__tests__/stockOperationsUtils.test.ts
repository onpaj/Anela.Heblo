import { isOperationStuck, canRetry } from "../stockOperationsUtils";
import { StockUpOperationState } from "../../api/generated/api-client";

const now = new Date("2026-01-01T12:00:00Z");
const minutesAgo = (n: number): Date => new Date(now.getTime() - n * 60 * 1000);

describe("isOperationStuck", () => {
  describe("Submitted", () => {
    it("is not stuck at 5 minutes", () => {
      expect(
        isOperationStuck({ state: StockUpOperationState.Submitted, submittedAt: minutesAgo(5) }, now),
      ).toBe(false);
    });

    it("is stuck at 6 minutes", () => {
      expect(
        isOperationStuck({ state: StockUpOperationState.Submitted, submittedAt: minutesAgo(6) }, now),
      ).toBe(true);
    });

    it("is not stuck without submittedAt", () => {
      expect(isOperationStuck({ state: StockUpOperationState.Submitted }, now)).toBe(false);
    });

    it("uses submittedAt, not an old createdAt", () => {
      expect(
        isOperationStuck(
          {
            state: StockUpOperationState.Submitted,
            createdAt: minutesAgo(1000),
            submittedAt: minutesAgo(1),
          },
          now,
        ),
      ).toBe(false);
    });
  });

  describe("Pending", () => {
    it("is not stuck at 10 minutes", () => {
      expect(
        isOperationStuck({ state: StockUpOperationState.Pending, createdAt: minutesAgo(10) }, now),
      ).toBe(false);
    });

    it("is stuck at 11 minutes", () => {
      expect(
        isOperationStuck({ state: StockUpOperationState.Pending, createdAt: minutesAgo(11) }, now),
      ).toBe(true);
    });

    it("ignores an old submittedAt", () => {
      expect(
        isOperationStuck(
          {
            state: StockUpOperationState.Pending,
            createdAt: minutesAgo(1),
            submittedAt: minutesAgo(1000),
          },
          now,
        ),
      ).toBe(false);
    });
  });

  it.each([StockUpOperationState.Completed, StockUpOperationState.Failed])(
    "is never stuck for terminal state %s",
    (state) => {
      expect(
        isOperationStuck({ state, createdAt: minutesAgo(1000), submittedAt: minutesAgo(1000) }, now),
      ).toBe(false);
    },
  );
});

describe("canRetry", () => {
  it.each([
    StockUpOperationState.Failed,
    StockUpOperationState.Submitted,
    StockUpOperationState.Pending,
  ])("returns true for %s", (state) => {
    expect(canRetry(state)).toBe(true);
  });

  it("returns false for Completed", () => {
    expect(canRetry(StockUpOperationState.Completed)).toBe(false);
  });

  it("returns false for undefined", () => {
    expect(canRetry(undefined)).toBe(false);
  });
});
