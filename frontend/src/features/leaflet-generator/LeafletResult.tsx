import { useState } from 'react';
import ReactMarkdown from 'react-markdown';

interface LeafletResultProps {
  content: string;
  onRegenerate: () => void;
}

export function LeafletResult({ content, onRegenerate }: LeafletResultProps) {
  const [copied, setCopied] = useState(false);

  if (!content) return null;

  const handleCopy = async () => {
    await navigator.clipboard.writeText(content);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="space-y-4">
      <div className="prose max-w-none">
        <ReactMarkdown>{content}</ReactMarkdown>
      </div>
      <div className="flex gap-2">
        <button
          type="button"
          onClick={handleCopy}
          className="px-4 py-2 text-sm font-medium border border-gray-300 rounded-md hover:bg-gray-50"
        >
          {copied ? 'Zkopírováno' : 'Kopírovat'}
        </button>
        <button
          type="button"
          onClick={onRegenerate}
          className="px-4 py-2 text-sm font-medium border border-gray-300 rounded-md hover:bg-gray-50"
        >
          Generovat znovu
        </button>
      </div>
    </div>
  );
}
