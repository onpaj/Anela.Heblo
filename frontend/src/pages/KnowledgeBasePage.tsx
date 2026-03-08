import React, { useState } from 'react';
import { Database, Search, MessageSquare } from 'lucide-react';
import KnowledgeBaseDocumentsTab from '../components/knowledge-base/KnowledgeBaseDocumentsTab';
import KnowledgeBaseSearchTab from '../components/knowledge-base/KnowledgeBaseSearchTab';
import KnowledgeBaseAskTab from '../components/knowledge-base/KnowledgeBaseAskTab';

type Tab = 'documents' | 'search' | 'ask';

const TABS: { id: Tab; label: string; Icon: React.FC<{ size: number }> }[] = [
  { id: 'documents', label: 'Dokumenty', Icon: Database },
  { id: 'search', label: 'Vyhledávání', Icon: Search },
  { id: 'ask', label: 'Dotaz AI', Icon: MessageSquare },
];

const KnowledgeBasePage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('documents');

  return (
    <div className="flex flex-col h-full p-6">
      <div className="mb-6">
        <h1 className="text-2xl font-semibold text-gray-900 flex items-center gap-2">
          <Database size={24} />
          Znalostní báze
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Firemní dokumenty indexované pro AI vyhledávání
        </p>
      </div>

      <div className="flex gap-1 mb-6 border-b border-gray-200">
        {TABS.map(({ id, label, Icon }) => (
          <button
            key={id}
            onClick={() => setActiveTab(id)}
            className={`flex items-center gap-2 px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
              activeTab === id
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            <Icon size={16} />
            {label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto">
        {activeTab === 'documents' && <KnowledgeBaseDocumentsTab />}
        {activeTab === 'search' && <KnowledgeBaseSearchTab />}
        {activeTab === 'ask' && <KnowledgeBaseAskTab />}
      </div>
    </div>
  );
};

export default KnowledgeBasePage;
