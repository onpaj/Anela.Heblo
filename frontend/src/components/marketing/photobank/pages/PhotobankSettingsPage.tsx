import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useMsal } from '@azure/msal-react';
import IndexRootsTab from '../settings/IndexRootsTab';
import TagRulesTab from '../settings/TagRulesTab';

const ADMIN_ROLE = 'administrator';

const PhotobankSettingsPage = () => {
  const { accounts } = useMsal();
  const isAdmin =
    (accounts[0]?.idTokenClaims as any)?.roles?.includes(ADMIN_ROLE) ?? false;

  const [activeTab, setActiveTab] = useState<'roots' | 'rules'>('roots');

  if (!isAdmin) {
    return (
      <div className="flex h-full items-center justify-center">
        <p className="text-gray-500 text-sm">403 – Přístup odepřen</p>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-5xl mx-auto">
      {/* Header */}
      <div className="flex items-center gap-3 mb-6">
        <Link to="/marketing/photobank" className="text-gray-500 hover:text-gray-700">
          ← Fotobanka
        </Link>
        <h1 className="text-xl font-semibold text-gray-900">Nastavení fotobanky</h1>
      </div>

      {/* Tab bar */}
      <div className="flex gap-1 border-b mb-6">
        <button
          onClick={() => setActiveTab('roots')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'roots'
              ? 'border-blue-600 text-blue-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Index Roots
        </button>
        <button
          onClick={() => setActiveTab('rules')}
          className={`px-4 py-2 text-sm font-medium border-b-2 -mb-px ${
            activeTab === 'rules'
              ? 'border-blue-600 text-blue-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          Tag Rules
        </button>
      </div>

      {/* Tab content */}
      {activeTab === 'roots' ? <IndexRootsTab /> : <TagRulesTab />}
    </div>
  );
};

export default PhotobankSettingsPage;
