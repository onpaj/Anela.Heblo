import React from 'react';
import { PAGE_CONTAINER_HEIGHT } from '../../../../constants/layout';
import ErrorState from '../../../common/ErrorState';
import { usePermissionsContext } from '../../../../auth/PermissionsContext';
import { useScreenView } from '../../../../telemetry/useScreenView';
import { useAdsSettingsQuery } from '../../../../api/hooks/useMarketingAdsSettings';
import { describeAdsError } from '../../../../api/hooks/marketingAds/errors';
import AdsLoading from '../shared/AdsLoading';
import { ADS_PERMISSIONS } from '../shared/permissions';
import AutonomyTable from './AutonomyTable';
import KillSwitchCard from './KillSwitchCard';
import LimitsTable from './LimitsTable';

const MarketingAdsSettingsPage: React.FC = () => {
  useScreenView('Marketing', 'MarketingAdsSettings');
  const { hasPermission } = usePermissionsContext();
  const isAdmin = hasPermission(ADS_PERMISSIONS.admin);
  const settings = useAdsSettingsQuery(isAdmin);

  if (!isAdmin) {
    return (
      <div className="flex h-full items-center justify-center">
        <p className="text-gray-500 dark:text-graphite-muted text-sm">403 – Přístup odepřen</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Nastavení reklam</h1>
        <p className="mt-1 text-gray-600 dark:text-graphite-muted">
          Kdy smí Heblo měnit reklamní účty: hlavní vypínač, režim autonomie a limity.
        </p>
      </div>
      <div className="flex-1 min-h-0 overflow-auto flex flex-col gap-4 pb-6">
        {settings.isLoading && <AdsLoading />}
        {settings.error && <ErrorState message={describeAdsError(settings.error)} className="py-8" />}
        {settings.data && (
          <>
            <KillSwitchCard state={settings.data.killSwitch} />
            <AutonomyTable rows={settings.data.autonomy} />
            <LimitsTable rows={settings.data.limits} />
          </>
        )}
      </div>
    </div>
  );
};

export default MarketingAdsSettingsPage;
