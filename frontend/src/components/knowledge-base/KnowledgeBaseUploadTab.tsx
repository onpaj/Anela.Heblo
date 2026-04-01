import React, { useCallback, useRef, useState } from 'react';
import { Upload, X, FileText } from 'lucide-react';
import {
  DocumentType,
  useUploadKnowledgeBaseDocumentMutation,
} from '../../api/hooks/useKnowledgeBase';

const ACCEPTED_EXTENSIONS = ['.pdf', '.docx', '.txt', '.md'];
const ACCEPTED_ATTR = ACCEPTED_EXTENSIONS.join(',');

const isAcceptedFile = (file: File): boolean => {
  const lower = file.name.toLowerCase();
  return ACCEPTED_EXTENSIONS.some(ext => lower.endsWith(ext));
};

const defaultDocumentType = (file: File): DocumentType => {
  const lower = file.name.toLowerCase();
  if (lower.endsWith('.txt') || lower.endsWith('.md')) return 'Conversation';
  return 'KnowledgeBase';
};

type FileStatus = 'waiting' | 'uploading' | 'done' | 'error';

const KnowledgeBaseUploadTab: React.FC = () => {
  const [dragOver, setDragOver] = useState(false);
  const [queuedFiles, setQueuedFiles] = useState<File[]>([]);
  const [fileStatuses, setFileStatuses] = useState<Record<string, FileStatus>>({});
  const [fileDocumentTypes, setFileDocumentTypes] = useState<Record<string, DocumentType>>({});
  const [isUploading, setIsUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const upload = useUploadKnowledgeBaseDocumentMutation();

  const addFiles = useCallback((incoming: FileList | null) => {
    if (!incoming) return;
    const accepted = Array.from(incoming).filter(isAcceptedFile);
    setQueuedFiles(prev => {
      const existingNames = new Set(prev.map(f => f.name));
      const newFiles = accepted.filter(f => !existingNames.has(f.name));
      return [...prev, ...newFiles];
    });
    setFileDocumentTypes(prev => {
      const next = { ...prev };
      for (const file of accepted) {
        if (!next[file.name]) {
          next[file.name] = defaultDocumentType(file);
        }
      }
      return next;
    });
  }, []);

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    addFiles(e.dataTransfer.files);
  }, [addFiles]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    addFiles(e.target.files);
    e.target.value = '';
  };

  const handleRemoveFile = (fileName: string) => {
    setQueuedFiles(prev => prev.filter(f => f.name !== fileName));
    setFileStatuses(prev => {
      const next = { ...prev };
      delete next[fileName];
      return next;
    });
    setFileDocumentTypes(prev => {
      const next = { ...prev };
      delete next[fileName];
      return next;
    });
  };

  const handleDocumentTypeChange = (fileName: string, value: DocumentType) => {
    setFileDocumentTypes(prev => ({ ...prev, [fileName]: value }));
  };

  const handleUpload = async () => {
    setIsUploading(true);
    const filesToProcess = queuedFiles.filter(f => fileStatuses[f.name] !== 'done');
    const outcomes: Record<string, 'done' | 'error'> = {};

    for (const file of filesToProcess) {
      setFileStatuses(prev => ({ ...prev, [file.name]: 'uploading' }));
      try {
        await upload.mutateAsync({
          file,
          documentType: fileDocumentTypes[file.name] ?? 'KnowledgeBase',
        });
        outcomes[file.name] = 'done';
        setFileStatuses(prev => ({ ...prev, [file.name]: 'done' }));
      } catch {
        outcomes[file.name] = 'error';
        setFileStatuses(prev => ({ ...prev, [file.name]: 'error' }));
      }
    }

    setIsUploading(false);
    setQueuedFiles(prev => prev.filter(f => outcomes[f.name] !== 'done'));
  };

  const handleCancelAll = () => {
    setQueuedFiles([]);
    setFileStatuses({});
    setFileDocumentTypes({});
  };

  const pendingCount = queuedFiles.filter(f => fileStatuses[f.name] !== 'done').length;

  const statusLabel = (status: FileStatus | undefined): React.ReactNode => {
    switch (status) {
      case 'uploading':
        return <span className="text-xs text-blue-600">Nahrávám…</span>;
      case 'done':
        return <span className="text-xs text-green-600">✅ Hotovo</span>;
      case 'error':
        return <span className="text-xs text-red-600">❌ Chyba</span>;
      default:
        return <span className="text-xs text-gray-400">Čeká</span>;
    }
  };

  return (
    <div className="space-y-4 max-w-lg">
      <div
        data-testid="drop-zone"
        onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
        className={`border-2 border-dashed rounded-xl p-12 text-center cursor-pointer transition-colors ${
          dragOver
            ? 'border-blue-400 bg-blue-50'
            : 'border-gray-300 hover:border-gray-400 bg-gray-50'
        }`}
      >
        <Upload className="w-10 h-10 text-gray-400 mx-auto mb-3" />
        <p className="text-sm font-medium text-gray-700">Přetáhněte soubory sem</p>
        <p className="text-xs text-gray-500 mt-1">nebo</p>
        <p className="text-sm text-blue-600 mt-1 font-medium">Vybrat soubory</p>
        <p className="text-xs text-gray-400 mt-3">Podporované formáty: PDF, DOCX, TXT, MD</p>
        <input
          ref={fileInputRef}
          type="file"
          accept={ACCEPTED_ATTR}
          multiple
          className="hidden"
          onChange={handleFileChange}
        />
      </div>

      {queuedFiles.length > 0 && (
        <div className="border border-gray-200 rounded-xl divide-y divide-gray-100">
          {queuedFiles.map(file => (
            <div key={file.name} className="flex items-center justify-between px-4 py-3 gap-2">
              <div className="flex items-center gap-2 min-w-0">
                <FileText className="w-4 h-4 text-gray-400 shrink-0" />
                <span className="text-sm text-gray-700 truncate">{file.name}</span>
              </div>
              <div className="flex items-center gap-2 shrink-0 ml-2">
                <select
                  value={fileDocumentTypes[file.name] ?? 'KnowledgeBase'}
                  onChange={(e) => handleDocumentTypeChange(file.name, e.target.value as DocumentType)}
                  disabled={isUploading}
                  className="text-xs border border-gray-200 rounded px-1 py-0.5 bg-white disabled:opacity-50"
                >
                  <option value="KnowledgeBase">Znalostní báze</option>
                  <option value="Conversation">Konverzace</option>
                </select>
                {statusLabel(fileStatuses[file.name])}
                <button
                  onClick={() => handleRemoveFile(file.name)}
                  disabled={isUploading}
                  className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
                  aria-label="Odebrat"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {queuedFiles.length > 0 && (
        <div className="flex gap-2">
          <button
            onClick={handleUpload}
            disabled={pendingCount === 0 || isUploading}
            className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
          >
            <Upload className="w-4 h-4" />
            Nahrát vše ({pendingCount})
          </button>
          <button
            onClick={handleCancelAll}
            disabled={isUploading}
            className="px-4 py-2 border border-gray-300 text-sm rounded-lg hover:bg-gray-50 disabled:opacity-50"
          >
            Zrušit vše
          </button>
        </div>
      )}
    </div>
  );
};

export default KnowledgeBaseUploadTab;
