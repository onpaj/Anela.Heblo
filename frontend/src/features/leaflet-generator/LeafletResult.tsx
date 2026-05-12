import { useState, useRef, useEffect } from 'react';
import ReactMarkdown from 'react-markdown';
import RagFeedbackForm from '../../components/feedback/RagFeedbackForm';
import { useSubmitLeafletFeedbackMutation } from '../../api/hooks/useLeaflet';

interface LeafletResultProps {
  content: string;
  generationId?: string | null;
  onRegenerate: () => void;
}

export default function LeafletResult({ content, generationId, onRegenerate }: LeafletResultProps) {
  const [copied, setCopied] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);
  const [alreadySubmitted, setAlreadySubmitted] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const submitFeedback = useSubmitLeafletFeedbackMutation();

  useEffect(() => {
    setIsSuccess(false);
    setAlreadySubmitted(false);
  }, [generationId]);

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

  const handleFeedbackSubmit = (data: { precisionScore: number; styleScore: number; comment?: string }) => {
    if (!generationId) return;
    submitFeedback.mutate(
      { generationId, precisionScore: data.precisionScore, styleScore: data.styleScore, comment: data.comment || undefined },
      {
        onSuccess: (result) => {
          if (result.alreadySubmitted) {
            setAlreadySubmitted(true);
          } else {
            setIsSuccess(true);
          }
        },
      }
    );
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
      {generationId && (
        <RagFeedbackForm
          onSubmit={handleFeedbackSubmit}
          isSubmitting={submitFeedback.isPending}
          isSuccess={isSuccess}
          alreadySubmitted={alreadySubmitted}
        />
      )}
    </div>
  );
}
