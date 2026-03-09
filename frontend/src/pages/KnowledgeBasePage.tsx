import React, { useState } from 'react';
import { Database } from 'lucide-react';
import KnowledgeBaseSearchAskTab from '../components/knowledge-base/KnowledgeBaseSearchAskTab';
import KnowledgeBaseDocumentsTab from '../components/knowledge-base/KnowledgeBaseDocumentsTab';
import KnowledgeBaseUploadTab from '../components/knowledge-base/KnowledgeBaseUploadTab';
import { useKnowledgeBaseUploadPermission } from '../api/hooks/useKnowledgeBase';

type Tab = 'search' | 'documents' | 'upload';

const KnowledgeBasePage: React.FC = () => {
  const canUpload = useKnowledgeBaseUploadPermission();
  const [activeTab, setActiveTab] = useState<Tab>('search');

  const tabs: { id: Tab; label: string }[] = [
    { id: 'search', label: 'Hledat' },
    { id: 'documents', label: 'Dokumenty' },
    ...(canUpload ? [{ id: 'upload' as Tab, label: 'Nahrát soubor' }] : []),
  ];

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center gap-3">
        <Database className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Znalostní báze</h1>
      </div>

      <div className="border-b border-gray-200">
        <nav className="flex gap-6" aria-label="Tabs">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      <div className="pt-2">
        {activeTab === 'search' && <KnowledgeBaseSearchAskTab />}
        {activeTab === 'documents' && <KnowledgeBaseDocumentsTab canDelete={canUpload} />}
        {activeTab === 'upload' && canUpload && (
          <KnowledgeBaseUploadTab onUploadSuccess={() => setActiveTab('documents')} />
        )}
      </div>
    </div>
  );
};

export default KnowledgeBasePage;
