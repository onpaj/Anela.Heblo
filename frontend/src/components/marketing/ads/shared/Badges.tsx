import React from 'react';
import { CHANGE_ORIGIN_LABELS, PLATFORM_LABELS, PROPOSAL_STATUS_LABELS, labelFor } from './adLabels';

export const NEUTRAL_PILL = 'bg-gray-100 text-gray-700 dark:bg-graphite-surface-2 dark:text-graphite-muted';
export const GREEN_PILL = 'bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300';
export const YELLOW_PILL = 'bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300';
export const BLUE_PILL = 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300';
export const RED_PILL = 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300';
const ORANGE_PILL = 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-300';
const PURPLE_PILL = 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-300';

const STATUS_PILLS: Readonly<Record<string, string>> = {
  Pending: YELLOW_PILL,
  Approved: BLUE_PILL,
  Executing: BLUE_PILL,
  Executed: GREEN_PILL,
  PartiallyExecuted: ORANGE_PILL,
  Failed: RED_PILL,
  Rejected: NEUTRAL_PILL,
  Expired: NEUTRAL_PILL,
  Reverted: PURPLE_PILL,
};

interface PillProps {
  className: string;
  children: React.ReactNode;
}

export const Pill: React.FC<PillProps> = ({ className, children }) => (
  <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${className}`}>{children}</span>
);

export const ProposalStatusBadge: React.FC<{ status: string }> = ({ status }) => (
  <Pill className={STATUS_PILLS[status] ?? NEUTRAL_PILL}>{labelFor(PROPOSAL_STATUS_LABELS, status)}</Pill>
);

export const PlatformBadge: React.FC<{ platform: string }> = ({ platform }) => (
  <Pill className={NEUTRAL_PILL}>{labelFor(PLATFORM_LABELS, platform)}</Pill>
);

export const OriginBadge: React.FC<{ origin: string }> = ({ origin }) => (
  <Pill className={origin === 'OutOfBand' ? ORANGE_PILL : GREEN_PILL}>{labelFor(CHANGE_ORIGIN_LABELS, origin)}</Pill>
);
