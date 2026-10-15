import React from 'react';
import { Bot } from 'lucide-react';

interface AgentReasoningBlockProps {
  reasoning: string;
  createdByKind: string;
}

// Free text from the proposal's author (usually an agent). Spec 6.1: shown separately and never
// used to build the diff. Rendered as a plain text node — React escapes it — so markup or
// instructions inside it are displayed, never interpreted.
const AgentReasoningBlock: React.FC<AgentReasoningBlockProps> = ({ reasoning, createdByKind }) => {
  const title = createdByKind === 'Human' ? 'Zdůvodnění autora' : 'Zdůvodnění agenta';
  const hasReasoning = reasoning.trim().length > 0;

  return (
    <section
      data-testid="agent-reasoning"
      aria-labelledby="agent-reasoning-title"
      className="rounded-md border border-dashed border-amber-400 dark:border-amber-700 bg-amber-50 dark:bg-amber-900/20 p-4"
    >
      <div className="flex items-center gap-2 mb-1">
        <Bot className="h-4 w-4 text-amber-700 dark:text-amber-300" aria-hidden="true" />
        <h3 id="agent-reasoning-title" className="text-sm font-semibold text-amber-900 dark:text-amber-200">
          {title}
        </h3>
      </div>
      <p className="text-xs text-amber-800 dark:text-amber-300 mb-2">
        Volný text autora návrhu. Heblo ho neověřuje — schvalujete seznam změn výše, ne tento text.
      </p>
      {hasReasoning ? (
        <p className="text-sm text-gray-800 dark:text-graphite-text whitespace-pre-wrap break-words">{reasoning}</p>
      ) : (
        <p className="text-sm italic text-gray-500 dark:text-graphite-muted">Autor neuvedl zdůvodnění.</p>
      )}
    </section>
  );
};

export default AgentReasoningBlock;
