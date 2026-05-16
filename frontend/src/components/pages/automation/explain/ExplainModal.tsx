import React, { useEffect } from 'react';
import { X } from 'lucide-react';

interface ExplainModalProps {
  isOpen: boolean;
  onClose: () => void;
  isLoading: boolean;
  relevantTranscript: string | null;
  explanation: string | null;
  error: string | null;
}

interface DialogTurn {
  speaker: string;
  text: string;
}

const SPEAKER_COLORS = [
  'bg-indigo-100 text-indigo-700',
  'bg-emerald-100 text-emerald-700',
  'bg-amber-100 text-amber-700',
  'bg-rose-100 text-rose-700',
];

function parseTranscriptToDialog(transcript: string): DialogTurn[] | null {
  // Split at "Speaker: " boundaries — speaker is a word starting with uppercase (incl. Czech)
  const parts = transcript.split(/([A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][^\n:]{0,25}):\s+/);
  if (parts.length < 3) return null;

  const turns: DialogTurn[] = [];
  for (let i = 1; i + 1 < parts.length; i += 2) {
    const speaker = parts[i].trim();
    const text = parts[i + 1].trim();
    if (speaker && text) turns.push({ speaker, text });
  }
  return turns.length > 1 ? turns : null;
}

export function ExplainModal({
  isOpen,
  onClose,
  isLoading,
  relevantTranscript,
  explanation,
  error,
}: ExplainModalProps) {
  useEffect(() => {
    if (!isOpen) return;
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const dialogTurns = relevantTranscript ? parseTranscriptToDialog(relevantTranscript) : null;

  const speakerColorMap = new Map<string, string>();
  if (dialogTurns) {
    let colorIdx = 0;
    for (const { speaker } of dialogTurns) {
      if (!speakerColorMap.has(speaker)) {
        speakerColorMap.set(speaker, SPEAKER_COLORS[colorIdx % SPEAKER_COLORS.length]);
        colorIdx++;
      }
    }
  }

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/40"
      role="dialog"
      aria-modal="true"
    >
      <div className="bg-white rounded-lg shadow-lg p-5 max-w-2xl w-full max-h-[80vh] flex flex-col">
        <div className="flex items-center justify-between mb-3 flex-shrink-0">
          <h3 className="text-base font-semibold text-gray-900">Detail vysvětlení</h3>
          <button
            type="button"
            title="Zavřít"
            onClick={onClose}
            className="p-1 rounded-md text-gray-500 hover:bg-gray-100"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-auto space-y-4">
          {isLoading && (
            <div className="flex justify-center py-8">
              <div
                role="status"
                className="w-8 h-8 border-4 border-indigo-200 border-t-indigo-600 rounded-full animate-spin"
                aria-label="Načítám..."
              />
            </div>
          )}

          {!isLoading && error && (
            <p className="text-sm text-red-600">{error}</p>
          )}

          {!isLoading && !error && !!relevantTranscript && (
            <>
              <div>
                <p className="text-xs font-semibold uppercase text-gray-500 mb-2">Záznam konverzace</p>
                {dialogTurns ? (
                  <div className="space-y-3">
                    {dialogTurns.map((turn, i) => (
                      <div key={i} className="flex flex-col gap-0.5">
                        <span className={`self-start text-xs font-semibold px-1.5 py-0.5 rounded ${speakerColorMap.get(turn.speaker)}`}>
                          {turn.speaker}
                        </span>
                        <p className="text-sm text-gray-800 pl-1">{turn.text}</p>
                      </div>
                    ))}
                  </div>
                ) : (
                  <div className="rounded-md border border-gray-200 bg-gray-50 p-3 text-sm text-gray-800 whitespace-pre-wrap">
                    {relevantTranscript}
                  </div>
                )}
              </div>
              <div>
                <p className="text-xs font-semibold uppercase text-gray-500 mb-1">Vysvětlení</p>
                <div className="rounded-md border border-blue-100 bg-blue-50 p-3 text-sm text-blue-900">
                  {explanation}
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
