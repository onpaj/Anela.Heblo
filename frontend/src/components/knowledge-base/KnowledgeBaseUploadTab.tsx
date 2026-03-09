import React, { useCallback, useRef, useState } from 'react';
import { Upload, X, FileText } from 'lucide-react';
import { useUploadKnowledgeBaseDocumentMutation } from '../../api/hooks/useKnowledgeBase';

const ACCEPTED_EXTENSIONS = '.pdf,.docx,.txt,.md';

interface Props {
  onUploadSuccess: () => void;
}

const KnowledgeBaseUploadTab: React.FC<Props> = ({ onUploadSuccess }) => {
  const [dragOver, setDragOver] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const upload = useUploadKnowledgeBaseDocumentMutation();

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const file = e.dataTransfer.files[0];
    if (file) setSelectedFile(file);
  }, []);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) setSelectedFile(file);
  };

  const handleUpload = async () => {
    if (!selectedFile) return;
    try {
      await upload.mutateAsync(selectedFile);
      setSelectedFile(null);
      onUploadSuccess();
    } catch {
      // Error displayed via upload.isError below
    }
  };

  const handleCancel = () => {
    setSelectedFile(null);
    upload.reset();
  };

  return (
    <div className="space-y-4 max-w-lg">
      {!selectedFile ? (
        <div
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
          <p className="text-sm font-medium text-gray-700">Přetáhněte soubor sem</p>
          <p className="text-xs text-gray-500 mt-1">nebo</p>
          <p className="text-sm text-blue-600 mt-1 font-medium">Vybrat soubor</p>
          <p className="text-xs text-gray-400 mt-3">Podporované formáty: PDF, DOCX, TXT, MD</p>
          <input
            ref={fileInputRef}
            type="file"
            accept={ACCEPTED_EXTENSIONS}
            className="hidden"
            onChange={handleFileChange}
          />
        </div>
      ) : (
        <div className="border border-gray-200 rounded-xl p-6 space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <FileText className="w-5 h-5 text-gray-500" />
              <span className="text-sm font-medium text-gray-700">{selectedFile.name}</span>
            </div>
            <button
              onClick={handleCancel}
              disabled={upload.isPending}
              className="text-gray-400 hover:text-gray-600 disabled:opacity-50"
            >
              <X className="w-4 h-4" />
            </button>
          </div>

          {upload.isPending && (
            <div className="w-full bg-gray-200 rounded-full h-1.5">
              <div className="bg-blue-600 h-1.5 rounded-full animate-pulse w-2/3" />
            </div>
          )}

          {upload.isError && (
            <p className="text-sm text-red-600">Nahrávání se nezdařilo. Zkuste to znovu.</p>
          )}

          <div className="flex gap-2">
            <button
              onClick={handleUpload}
              disabled={upload.isPending}
              className="flex items-center gap-1 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
            >
              <Upload className="w-4 h-4" />
              Nahrát
            </button>
            <button
              onClick={handleCancel}
              disabled={upload.isPending}
              className="px-4 py-2 border border-gray-300 text-sm rounded-lg hover:bg-gray-50 disabled:opacity-50"
            >
              Zrušit
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default KnowledgeBaseUploadTab;
