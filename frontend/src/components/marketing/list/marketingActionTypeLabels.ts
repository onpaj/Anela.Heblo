import { MarketingActionType } from '../../../api/generated/api-client';

export const ACTION_TYPE_LABELS: Record<MarketingActionType, string> = {
  [MarketingActionType.SocialMedia]: 'Sociální sítě',
  [MarketingActionType.Blog]: 'Blog',
  [MarketingActionType.Newsletter]: 'Newsletter',
  [MarketingActionType.PR]: 'PR',
  [MarketingActionType.Event]: 'Událost',
  [MarketingActionType.Meeting]: 'Meeting',
};

export const ACTION_TYPE_BADGE: Record<MarketingActionType, string> = {
  [MarketingActionType.SocialMedia]: 'bg-yellow-100 text-yellow-800',
  [MarketingActionType.Blog]: 'bg-green-100 text-green-800',
  [MarketingActionType.Newsletter]: 'bg-purple-100 text-purple-800',
  [MarketingActionType.PR]: 'bg-orange-100 text-orange-800',
  [MarketingActionType.Event]: 'bg-red-100 text-red-800',
  [MarketingActionType.Meeting]: 'bg-teal-100 text-teal-800',
};

export const ALL_ACTION_TYPE_OPTIONS: ReadonlyArray<MarketingActionType> = [
  MarketingActionType.SocialMedia,
  MarketingActionType.Blog,
  MarketingActionType.Newsletter,
  MarketingActionType.PR,
  MarketingActionType.Event,
  MarketingActionType.Meeting,
];
