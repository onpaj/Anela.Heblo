import React, { useState } from 'react';
import { FileText } from 'lucide-react';
import LeafletGenerateTab from './LeafletGenerateTab';
import LeafletDocumentsTab from './LeafletDocumentsTab';
import LeafletUploadTab from './LeafletUploadTab';
import { useMarketingWriterPermission } from '../../api/hooks/useMarketingWriterPermission';
import { useScreenView } from '../../telemetry/useScreenView';

type Tab = 'generate' | 'documents' | 'upload';

export default function LeafletGeneratorPage() {
  const canUpload = useMarketingWriterPermission();
  const [activeTab, setActiveTab] = useState<Tab>('generate');

  useScreenView('Marketing', 'LeafletGenerator');

  const tabs: { id: Tab; label: string }[] = [
    { id: 'generate', label: 'Generovat' },
    { id: 'documents', label: 'Dokumenty' },
    ...(canUpload ? [{ id: 'upload' as Tab, label: 'Nahrát soubor' }] : []),
  ];

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center gap-3">
        <FileText className="w-6 h-6 text-blue-600" />
        <h1 className="text-2xl font-semibold text-gray-900">Generátor letáků</h1>
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
        {activeTab === 'generate' && <LeafletGenerateTab />}
        {activeTab === 'documents' && <LeafletDocumentsTab canDelete={canUpload} />}
        {activeTab === 'upload' && canUpload && <LeafletUploadTab />}
      </div>
    </div>
  );
}
