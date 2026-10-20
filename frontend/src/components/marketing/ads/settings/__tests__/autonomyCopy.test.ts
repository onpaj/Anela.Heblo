import { describeModeChange } from '../autonomyCopy';
import type { AdAutonomySetting } from '../../../../../api/hooks/marketingAds/types';

const ROW: AdAutonomySetting = {
  platform: 'GoogleAds',
  actionType: 'AddNegativeKeyword',
  mode: 'ProposeOnly',
  approvedWithoutEditRate: null,
  decidedCount: 0,
};

describe('describeModeChange', () => {
  it('appends the consequence of a known target mode', () => {
    expect(describeModeChange(ROW, 'ProposeOnly')).toBe(
      'Google Ads – Vylučující klíčová slova: Jen návrhy → Jen návrhy. Každý návrh bude čekat na schválení člověkem.',
    );
  });

  it('leaves no dangling separator for a target mode without a known consequence', () => {
    expect(describeModeChange(ROW, 'Legacy')).toBe('Google Ads – Vylučující klíčová slova: Jen návrhy → Legacy');
  });
});
