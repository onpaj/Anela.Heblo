import React, { useState } from 'react';
import LeafletForm from './LeafletForm';
import LeafletResult from './LeafletResult';
import { useGenerateLeafletMutation } from '../../api/hooks/useLeaflet';
import {
  AudienceType,
  ErrorCodes,
  GenerateLeafletResponse,
  LeafletLength,
} from '../../api/generated/api-client';

interface ErrorBanner {
  kind: 'insufficient' | 'transient';
  message: string;
}

const LeafletGenerateTab: React.FC = () => {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
  const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
  const [result, setResult] = useState('');
  const [generationId, setGenerationId] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);
  const generateLeaflet = useGenerateLeafletMutation();

  const generate = async () => {
    setGenerationId(null);
    setErrorBanner(null);
    try {
      const response = await generateLeaflet.mutateAsync({ topic, audience, length });
      setResult(response.content ?? '');
      setGenerationId(response.id ?? null);
    } catch (err: unknown) {
      if (err instanceof GenerateLeafletResponse && err.errorCode === ErrorCodes.LeafletEmptyRetrieval) {
        setErrorBanner({
          kind: 'insufficient',
          message: 'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
        });
      } else {
        setErrorBanner({
          kind: 'transient',
          message: 'Generování selhalo. Zkuste to prosím znovu.',
        });
      }
    }
  };

  return (
    <>
      {errorBanner && (
        <div
          role="alert"
          className={`mb-4 rounded p-3 text-sm ${
            errorBanner.kind === 'insufficient'
              ? 'bg-amber-100 text-amber-900 dark:bg-amber-900/30 dark:text-amber-300'
              : 'bg-red-100 text-red-900 dark:bg-red-900/30 dark:text-red-300'
          }`}
        >
          {errorBanner.message}
        </div>
      )}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div>
          <LeafletForm
            topic={topic}
            audience={audience}
            length={length}
            isLoading={generateLeaflet.isPending}
            onTopicChange={setTopic}
            onAudienceChange={setAudience}
            onLengthChange={setLength}
            onSubmit={generate}
          />
        </div>
        <div>
          {generateLeaflet.isPending ? (
            <div className="animate-pulse space-y-2">
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-3/4" />
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded" />
              <div className="h-4 bg-gray-200 dark:bg-graphite-hover rounded w-5/6" />
            </div>
          ) : (
            <LeafletResult content={result} generationId={generationId} onRegenerate={generate} />
          )}
        </div>
      </div>
    </>
  );
};

export default LeafletGenerateTab;
