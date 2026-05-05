import { useState, useRef, useEffect } from 'react';
import ReactMarkdown from 'react-markdown';

interface LeafletResultProps {
  content: string;
  onRegenerate: () => void;
}

export default function LeafletResult({ content, onRegenerate }: LeafletResultProps) {
  const [copied, setCopied] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  if (!content) return null;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(content);
      if (timerRef.current) clearTimeout(timerRef.current);
      setCopied(true);
      timerRef.current = setTimeout(() => setCopied(false), 2000);
    } catch {
      // clipboard unavailable — no feedback change
    }
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
