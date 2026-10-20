import { ACTION_TYPE_LABELS, PLATFORM_LABELS, PROPOSAL_STATUS_LABELS, labelFor } from '../adLabels';

describe('labelFor', () => {
  it('returns the Czech label for a known value', () => {
    expect(labelFor(PLATFORM_LABELS, 'GoogleAds')).toBe('Google Ads');
    expect(labelFor(PROPOSAL_STATUS_LABELS, 'Pending')).toBe('Čeká na schválení');
    expect(labelFor(ACTION_TYPE_LABELS, 'PauseAd')).toBe('Pozastavení reklam');
  });

  it('falls back to the raw value for a value the UI does not know yet', () => {
    expect(labelFor(ACTION_TYPE_LABELS, 'ChangeBudget')).toBe('ChangeBudget');
  });

  it('renders a dash for an empty value', () => {
    expect(labelFor(PLATFORM_LABELS, '')).toBe('—');
    expect(labelFor(PLATFORM_LABELS, null)).toBe('—');
  });
});
