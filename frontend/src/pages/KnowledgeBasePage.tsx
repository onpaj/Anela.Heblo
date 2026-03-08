import React, { useState } from 'react';
import { Database } from 'lucide-react';
import KnowledgeBaseDocumentsTab from '../components/knowledge-base/KnowledgeBaseDocumentsTab';
import KnowledgeBaseSearchTab from '../components/knowledge-base/KnowledgeBaseSearchTab';
import KnowledgeBaseAskTab from '../components/knowledge-base/KnowledgeBaseAskTab';

type Tab = 'documents' | 'search' | 'ask';

const TABS: { id: Tab; label: string }[] = [
  { id: 'documents', label: 'Dokumenty' },
  { id: 'search', label: 'Vyhledávání' },
  { id: 'ask', label: 'Dotaz AI' },
];

const KnowledgeBasePage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('documents');

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center gap-3">
        <Database className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Znalostní báze</h1>
      </div>

      <div className="border-b border-gray-200">
        <nav className="-mb-px flex gap-6">
          {TABS.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-3 text-sm font-medium border-b-2 transition-colors ${
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

      <div>
        {activeTab === 'documents' && <KnowledgeBaseDocumentsTab />}
        {activeTab === 'search' && <KnowledgeBaseSearchTab />}
        {activeTab === 'ask' && <KnowledgeBaseAskTab />}
      </div>
    </div>
  );
};

export default KnowledgeBasePage;
