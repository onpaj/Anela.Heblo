import { Sparkles } from "lucide-react";
import { DRAFT_REPLY_HINTS } from "./draftReplyHints";

interface DraftReplyTriggerBarProps {
  disabled: boolean;
  canGenerateWithoutTopic: boolean;
  error: string | null;
  onGenerate: (topic?: string) => void;
}

function DraftReplyTriggerBar({
  disabled,
  canGenerateWithoutTopic,
  error,
  onGenerate,
}: DraftReplyTriggerBarProps) {
  return (
    <div className="border-t border-gray-100 bg-gray-50 px-4 py-2">
      <div className="flex flex-wrap items-center gap-2">
        {DRAFT_REPLY_HINTS.map((hint) => (
          <button
            key={hint.id}
            type="button"
            disabled={disabled}
            onClick={() => onGenerate(hint.label)}
            className="inline-flex items-center rounded-full px-3 py-1 text-xs bg-white border border-gray-200 text-gray-700 hover:bg-blue-50 hover:border-blue-300 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {hint.label}
          </button>
        ))}
        <button
          type="button"
          disabled={disabled || !canGenerateWithoutTopic}
          onClick={() => onGenerate(undefined)}
          title={
            canGenerateWithoutTopic
              ? undefined
              : "Konverzace neobsahuje zprávu zákazníka"
          }
          className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium bg-blue-500 text-white hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Sparkles className="w-3.5 h-3.5" />
          Generovat odpověď
        </button>
      </div>
      {error && <p className="mt-1.5 text-xs text-red-600">{error}</p>}
    </div>
  );
}

export default DraftReplyTriggerBar;
