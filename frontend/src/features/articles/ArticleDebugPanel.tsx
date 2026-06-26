import { useState } from 'react';
import { ChevronDown, ChevronRight, Loader2 } from 'lucide-react';
import { ArticleGenerationStepStatus } from '../../api/generated/api-client';
import { useArticleTraceQuery, ArticleGenerationStep } from '../../api/hooks/useArticleTrace';

interface ArticleDebugPanelProps {
  articleId: string;
}

const STEP_STATUS_COLORS: Record<ArticleGenerationStepStatus, string> = {
  [ArticleGenerationStepStatus.Running]: 'bg-blue-100 text-blue-700',
  [ArticleGenerationStepStatus.Succeeded]: 'bg-green-100 text-green-700',
  [ArticleGenerationStepStatus.Failed]: 'bg-red-100 text-red-700',
};

const STEP_STATUS_LABELS: Record<ArticleGenerationStepStatus, string> = {
  [ArticleGenerationStepStatus.Running]: 'Běží',
  [ArticleGenerationStepStatus.Succeeded]: 'Dokončeno',
  [ArticleGenerationStepStatus.Failed]: 'Chyba',
};

function PrettyJson({ raw }: { raw: string }) {
  try {
    const parsed = JSON.parse(raw);
    return <pre className="text-xs overflow-auto max-h-64 bg-gray-50 rounded p-2">{JSON.stringify(parsed, null, 2)}</pre>;
  } catch {
    return <pre className="text-xs overflow-auto max-h-64 bg-gray-50 rounded p-2">{raw}</pre>;
  }
}

function StepCard({ step }: { step: ArticleGenerationStep }) {
  const colorClass = STEP_STATUS_COLORS[step.status] ?? 'bg-gray-100 text-gray-700';
  const label = STEP_STATUS_LABELS[step.status] ?? step.status;

  return (
    <div className="border rounded p-3 space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-xs font-medium text-gray-500">#{step.sequence}</span>
        <span className="text-sm font-semibold text-gray-800">{step.stepName}</span>
        <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${colorClass}`}>
          {step.status === ArticleGenerationStepStatus.Running && <Loader2 className="w-3 h-3 animate-spin" />}
          {label}
        </span>
        {step.model && (
          <span className="text-xs text-gray-500 ml-auto">{step.model}</span>
        )}
        {step.durationMs != null && (
          <span className="text-xs text-gray-400">{step.durationMs} ms</span>
        )}
      </div>

      {step.status === ArticleGenerationStepStatus.Failed && step.errorMessage && (
        <p className="text-xs text-red-600 bg-red-50 rounded p-2">{step.errorMessage}</p>
      )}

      {step.inputJson && (
        <details className="text-xs">
          <summary className="cursor-pointer text-gray-500 hover:text-gray-700 select-none">Vstup (InputJson)</summary>
          <PrettyJson raw={step.inputJson} />
        </details>
      )}

      {step.outputJson && (
        <details className="text-xs">
          <summary className="cursor-pointer text-gray-500 hover:text-gray-700 select-none">Výstup (OutputJson)</summary>
          <PrettyJson raw={step.outputJson} />
        </details>
      )}
    </div>
  );
}

export default function ArticleDebugPanel({ articleId }: ArticleDebugPanelProps) {
  const [expanded, setExpanded] = useState(false);
  const { data, isLoading, error } = useArticleTraceQuery(articleId, expanded);

  return (
    <div className="mt-6 border-t pt-4">
      <button
        type="button"
        className="flex items-center gap-1 text-sm font-semibold text-gray-600 hover:text-gray-800 select-none"
        onClick={() => setExpanded((prev) => !prev)}
      >
        {expanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
        Debug — průběh generování
      </button>

      {expanded && (
        <div className="mt-3 space-y-2">
          {isLoading && (
            <div className="flex justify-center py-4">
              <Loader2 className="w-5 h-5 animate-spin text-gray-400" />
            </div>
          )}
          {error && (
            <p className="text-xs text-red-600">Nepodařilo se načíst průběh generování.</p>
          )}
          {data && data.steps.length === 0 && (
            <p className="text-xs text-gray-500">Žádné kroky k zobrazení.</p>
          )}
          {data && data.steps.map((step) => (
            <StepCard key={step.id} step={step} />
          ))}
        </div>
      )}
    </div>
  );
}
