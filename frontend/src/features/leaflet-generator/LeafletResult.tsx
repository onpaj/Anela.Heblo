import { useState, useRef, useEffect } from 'react';
import ReactMarkdown from 'react-markdown';
import RagFeedbackForm from '../../components/feedback/RagFeedbackForm';
import { useSubmitLeafletFeedbackMutation } from '../../api/hooks/useLeaflet';

interface LeafletResultProps {
  content: string;
  onRegenerate: () => void;
  generationId?: string;
}

export default function LeafletResult({ content, onRegenerate, generationId }: LeafletResultProps) {
  const [copied, setCopied] = useState(false);
  const [feedbackState, setFeedbackState] = useState<'idle' | 'submitted' | 'alreadySubmitted'>(
    'idle',
  );
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const submitFeedback = useSubmitLeafletFeedbackMutation();

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  useEffect(() => {
    setFeedbackState('idle');
  }, [generationId]);

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
      {generationId && (
        <RagFeedbackForm
          onSubmit={(payload) => {
            submitFeedback.mutate(
              {
                generationId,
                ...payload,
              },
              {
                onSuccess: (result) => {
                  if (result.alreadySubmitted) {
                    setFeedbackState('alreadySubmitted');
                  } else {
                    setFeedbackState('submitted');
                  }
                },
              },
            );
          }}
          isSubmitting={submitFeedback.isPending}
          alreadySubmitted={feedbackState === 'alreadySubmitted'}
          isSuccess={feedbackState === 'submitted'}
        />
      )}
    </div>
  );
}
