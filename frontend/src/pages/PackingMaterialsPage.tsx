import React, { useState } from 'react';
import { Package } from 'lucide-react';
import PackingMaterialsSettingsTab from '../components/packing-materials/PackingMaterialsSettingsTab';
import ConsumptionHistoryTab from '../components/packing-materials/ConsumptionHistoryTab';

type Tab = 'settings' | 'history';

const PackingMaterialsPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('settings');

  const tabs: { id: Tab; label: string }[] = [
    { id: 'settings', label: 'Nastavení' },
    { id: 'history', label: 'Historie spotřeby' },
  ];

  return (
    <div className="p-6">
      <div className="flex items-center space-x-3 mb-4">
        <Package className="h-8 w-8 text-gray-700 dark:text-graphite-muted" />
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-graphite-text">Sledování materiálů</h1>
          <p className="text-sm text-gray-500 dark:text-graphite-muted">Správa spotřebních materiálů a historie spotřeby</p>
        </div>
      </div>

      <div className="border-b border-gray-200 dark:border-graphite-border mb-6">
        <nav className="flex gap-6" aria-label="Tabs">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600 dark:text-graphite-accent dark:border-graphite-accent'
                  : 'border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'settings' && <PackingMaterialsSettingsTab />}
      {activeTab === 'history' && <ConsumptionHistoryTab />}
    </div>
  );
};

export default PackingMaterialsPage;
