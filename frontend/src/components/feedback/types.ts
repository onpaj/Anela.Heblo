// frontend/src/components/feedback/types.ts
import type { ReactNode } from 'react';

export interface GenericFeedbackStats {
  totalItems: number;
  totalWithFeedback: number;
  avgPrecisionScore: number | null;
  avgStyleScore: number | null;
}

export interface FeedbackRow {
  id: string;
  primaryText: string;
  secondaryText?: string;
  createdAt: string;
  userId?: string;
  userName?: string;
  precisionScore?: number | null;
  styleScore?: number | null;
  hasFeedback: boolean;
}

export interface FeedbackDetail extends FeedbackRow {
  feedbackComment?: string | null;
  extra?: ReactNode;
}

export interface GenericFeedbackParams {
  pageNumber: number;
  pageSize: number;
  sortBy: string;
  sortDescending: boolean;
  hasFeedback?: boolean;
  userId?: string;
}

export const DEFAULT_FEEDBACK_PARAMS: GenericFeedbackParams = {
  pageNumber: 1,
  pageSize: 20,
  sortBy: 'CreatedAt',
  sortDescending: true,
};

export const SORT_COLUMNS = [
  { value: 'CreatedAt', label: 'Datum' },
  { value: 'PrecisionScore', label: 'Přesnost' },
  { value: 'StyleScore', label: 'Styl' },
] as const;
