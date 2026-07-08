import React from 'react';
import type { RagFeedbackLogSummary } from './ragFeedbackTypes';

interface RagFeedbackDetailExtraProps {
  log: RagFeedbackLogSummary;
}

const CONTENT_PREVIEW_LENGTH = 400;

const Field: React.FC<{ label: React.ReactNode; children: React.ReactNode }> = ({ label, children }) => (
  <div>
    <p className="text-xs text-gray-500 dark:text-graphite-muted uppercase tracking-wide mb-1">{label}</p>
    <div className="text-sm text-gray-900 dark:text-graphite-text">{children}</div>
  </div>
);

/**
 * Renders the rich RAG eval fields inside the feedback detail modal (via FeedbackDetail.extra):
 * metrics, expanded query, retrieved chunks + scores, the rendered system prompt, and — for
 * Smartsupp draft replies — the topic and the actually-sent answer with an "edited" badge.
 */
const RagFeedbackDetailExtra: React.FC<RagFeedbackDetailExtraProps> = ({ log }) => {
  const isSmartsupp = log.conversationId != null || log.sentAnswer != null || log.topic != null;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-3 gap-4">
        <Field label="TopK">{log.topK}</Field>
        <Field label="Zdrojů">{log.sourceCount}</Field>
        <Field label="Doba odezvy">{log.durationMs} ms</Field>
      </div>

      {isSmartsupp && (log.topic || log.conversationId) && (
        <div className="grid grid-cols-2 gap-4">
          {log.topic && <Field label="Téma">{log.topic}</Field>}
          {log.conversationId && <Field label="Konverzace">{log.conversationId}</Field>}
        </div>
      )}

      {isSmartsupp && log.sentAnswer != null && (
        <Field label="Odeslaná odpověď">
          <div className="space-y-1">
            {log.wasEdited != null && (
              <span
                className={`inline-block text-xs px-1.5 py-0.5 rounded font-medium ${
                  log.wasEdited
                    ? 'bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300'
                    : 'bg-gray-100 text-gray-600 dark:bg-graphite-surface-2 dark:text-graphite-muted'
                }`}
              >
                {log.wasEdited ? 'Upraveno před odesláním' : 'Odesláno beze změny'}
              </span>
            )}
            <p className="whitespace-pre-wrap text-gray-900 dark:text-graphite-text">{log.sentAnswer}</p>
          </div>
        </Field>
      )}

      {log.expandedQuery && (
        <Field label="Rozšířený dotaz">
          <p className="whitespace-pre-wrap text-gray-700 dark:text-graphite-muted">{log.expandedQuery}</p>
        </Field>
      )}

      {log.retrievedChunks.length > 0 && (
        <div>
          <p className="text-xs text-gray-500 dark:text-graphite-muted uppercase tracking-wide mb-2">
            Zdroje ({log.retrievedChunks.length})
          </p>
          <div className="space-y-2">
            {log.retrievedChunks.map((chunk, idx) => (
              <div
                key={`${chunk.chunkId}-${idx}`}
                className="border border-gray-200 dark:border-graphite-border rounded-lg px-3 py-2 space-y-1"
              >
                <div className="flex items-center justify-between">
                  <span className="text-sm font-medium text-gray-700 dark:text-graphite-muted">
                    {chunk.filename || '—'}
                  </span>
                  <span className="text-xs px-1.5 py-0.5 rounded font-medium bg-gray-100 text-gray-600 dark:bg-graphite-surface-2 dark:text-graphite-muted">
                    {Math.round(chunk.score * 100)}%
                  </span>
                </div>
                <p className="text-xs text-gray-500 dark:text-graphite-muted whitespace-pre-wrap">
                  {chunk.content.length > CONTENT_PREVIEW_LENGTH
                    ? `${chunk.content.slice(0, CONTENT_PREVIEW_LENGTH)}…`
                    : chunk.content}
                </p>
              </div>
            ))}
          </div>
        </div>
      )}

      {log.systemPrompt && (
        <details className="border border-gray-200 dark:border-graphite-border rounded-lg">
          <summary className="cursor-pointer px-3 py-2 text-xs text-gray-500 dark:text-graphite-muted uppercase tracking-wide">
            Systémový prompt
          </summary>
          <pre className="max-h-80 overflow-auto px-3 py-2 text-xs text-gray-700 dark:text-graphite-muted whitespace-pre-wrap">
            {log.systemPrompt}
          </pre>
        </details>
      )}
    </div>
  );
};

export default RagFeedbackDetailExtra;
