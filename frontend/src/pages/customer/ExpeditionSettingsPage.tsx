import { useSearchParams } from 'react-router-dom';
import { Settings, Thermometer, Gift } from 'lucide-react';
import { PAGE_CONTAINER_HEIGHT } from '../../constants/layout';
import CoolingTab from '../../components/customer/expeditionSettings/CoolingTab';
import GiftsTab from '../../components/customer/expeditionSettings/GiftsTab';
import { useScreenView } from '../../telemetry/useScreenView';

type Tab = 'cooling' | 'gifts';

function ExpeditionSettingsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab: Tab = (searchParams.get('tab') as Tab) ?? 'cooling';
  useScreenView('Customer', 'ExpeditionSettings', activeTab === 'cooling' ? 'CoolingTab' : 'GiftsTab');

  const handleTabChange = (tab: Tab) => {
    setSearchParams({ tab });
  };

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      <div className="flex-shrink-0 px-4 py-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text flex items-center gap-3">
          <Settings className="h-6 w-6 text-indigo-600 dark:text-graphite-accent" />
          Nastavení expedice
        </h1>
        <p className="text-sm text-gray-500 dark:text-graphite-muted mt-1">
          Konfigurace chlazení a dárků pro expedici.
        </p>
      </div>

      <div className="flex-shrink-0 flex border-b border-gray-200 dark:border-graphite-border px-4">
        <button
          onClick={() => handleTabChange('cooling')}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === 'cooling'
              ? 'border-indigo-500 text-indigo-600 dark:border-graphite-accent dark:text-graphite-accent'
              : 'border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700'
          }`}
        >
          <Thermometer className="h-4 w-4" />
          <span>Chlazení</span>
        </button>

        <button
          onClick={() => handleTabChange('gifts')}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === 'gifts'
              ? 'border-indigo-500 text-indigo-600 dark:border-graphite-accent dark:text-graphite-accent'
              : 'border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700'
          }`}
        >
          <Gift className="h-4 w-4" />
          <span>Dárky</span>
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
        {activeTab === 'cooling' ? <CoolingTab /> : <GiftsTab />}
      </div>
    </div>
  );
}

export default ExpeditionSettingsPage;
