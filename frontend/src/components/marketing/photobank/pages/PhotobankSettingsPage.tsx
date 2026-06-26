import { useState } from 'react';
import { Link } from 'react-router-dom';
import { usePermissionsContext } from '../../../../auth/PermissionsContext';
import IndexRootsTab from '../settings/IndexRootsTab';
import TagRulesTab from '../settings/TagRulesTab';
import TagsTab from '../settings/TagsTab';
import { useScreenView } from '../../../../telemetry/useScreenView';

const PhotobankSettingsPage = () => {
  const { hasPermission } = usePermissionsContext();
  const isAdmin = hasPermission('marketing.photobank.admin');

  const [activeTab, setActiveTab] = useState<'roots' | 'rules' | 'tags'>('roots');

  useScreenView('Marketing', 'PhotobankSettings');

  if (!isAdmin) {
    return (
      <div className="flex h-full items-center justify-center">
        <p className="text-gray-500 dark:text-graphite-muted text-sm">403 – Přístup odepřen</p>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-5xl mx-auto">
      {/* Header */}
      <div className="flex items-center gap-3 mb-6">
        <Link to="/marketing/photobank" className="text-gray-500 dark:text-graphite-muted hover:text-gray-700">
          ← Fotobanka
        </Link>
        <h1 className="text-xl font-semibold text-gray-900 dark:text-graphite-text">Nastavení fotobanky</h1>
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 border-b dark:border-graphite-border mb-6">
        <button
          onClick={() => setActiveTab('roots')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'roots'
              ? 'border-blue-600 dark:border-graphite-accent text-blue-600 dark:text-graphite-accent'
              : 'border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700'
          }`}
        >
          Index Roots
        </button>
        <button
          onClick={() => setActiveTab('rules')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'rules'
              ? 'border-blue-600 dark:border-graphite-accent text-blue-600 dark:text-graphite-accent'
              : 'border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700'
          }`}
        >
          Tag Rules
        </button>
        <button
          onClick={() => setActiveTab('tags')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'tags'
              ? 'border-blue-600 dark:border-graphite-accent text-blue-600 dark:text-graphite-accent'
              : 'border-transparent text-gray-500 dark:text-graphite-muted hover:text-gray-700'
          }`}
        >
          Štítky
        </button>
      </div>

      {/* Tab content */}
      {activeTab === 'roots' && <IndexRootsTab />}
      {activeTab === 'rules' && <TagRulesTab />}
      {activeTab === 'tags' && <TagsTab />}
    </div>
  );
};

export default PhotobankSettingsPage;
