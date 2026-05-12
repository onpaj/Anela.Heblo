import React, { useState } from 'react';
import LeafletForm from './LeafletForm';
import LeafletResult from './LeafletResult';
import { getAuthenticatedApiClient } from '../../api/client';
import { AudienceType, GenerateLeafletRequest, LeafletLength } from '../../api/generated/api-client';

interface ErrorBanner {
  kind: 'insufficient' | 'transient';
  message: string;
}

interface ApiError {
  status: number;
  detail?: string;
}

function isApiError(err: unknown): err is ApiError {
  return typeof err === 'object' && err !== null && typeof (err as Record<string, unknown>)['status'] === 'number';
}

const LeafletGenerateTab: React.FC = () => {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
  const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
  const [result, setResult] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [generationId, setGenerationId] = useState<string | null>(null);
  const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);

  const generate = async () => {
    setIsLoading(true);
    setGenerationId(null);
    setErrorBanner(null);
    try {
      const client = getAuthenticatedApiClient();
      const response = await client.leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }));
      setResult(response.content ?? '');
      setGenerationId((response as any).id ?? null);
    } catch (err: unknown) {
      if (isApiError(err) && err.status === 422) {
        setErrorBanner({
          kind: 'insufficient',
          message:
            err.detail ??
            'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
        });
      } else {
        setErrorBanner({
          kind: 'transient',
          message: 'Generování selhalo. Zkuste to prosím znovu.',
        });
      }
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <>
      {errorBanner && (
        <div
          role="alert"
          className={`mb-4 rounded p-3 text-sm ${
            errorBanner.kind === 'insufficient'
              ? 'bg-amber-100 text-amber-900'
              : 'bg-red-100 text-red-900'
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
            isLoading={isLoading}
            onTopicChange={setTopic}
            onAudienceChange={setAudience}
            onLengthChange={setLength}
            onSubmit={generate}
          />
        </div>
        <div>
          {isLoading ? (
            <div className="animate-pulse space-y-2">
              <div className="h-4 bg-gray-200 rounded w-3/4" />
              <div className="h-4 bg-gray-200 rounded" />
              <div className="h-4 bg-gray-200 rounded w-5/6" />
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
