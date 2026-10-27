import { differenceInMinutes } from "date-fns";
import { StockUpOperationState } from "../api/generated/api-client";

export const isOperationStuck = (
  operation: {
    state?: StockUpOperationState;
    submittedAt?: Date | string;
    createdAt?: Date | string;
  },
  now: Date = new Date(),
): boolean => {
  if (operation.state === StockUpOperationState.Submitted && operation.submittedAt) {
    const minutesSinceSubmit = differenceInMinutes(now, new Date(operation.submittedAt));
    return minutesSinceSubmit > 5;
  }
  if (operation.state === StockUpOperationState.Pending) {
    const minutesSinceCreation = differenceInMinutes(now, new Date(operation.createdAt as Date | string));
    return minutesSinceCreation > 10;
  }
  return false;
};

export const canRetry = (state?: StockUpOperationState): boolean => {
  if (!state) return false;
  return (
    state === StockUpOperationState.Failed ||
    state === StockUpOperationState.Submitted ||
    state === StockUpOperationState.Pending
  );
};
