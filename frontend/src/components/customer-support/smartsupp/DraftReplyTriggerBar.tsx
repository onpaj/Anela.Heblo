import { useState } from "react";
import { Sparkles, Tag } from "lucide-react";
import { DRAFT_REPLY_HINTS } from "./draftReplyHints";
import TopicPickerSheet from "./TopicPickerSheet";

const NO_CUSTOMER_MESSAGE_TOOLTIP = "Konverzace neobsahuje zprávu zákazníka";

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
  const [topicPickerOpen, setTopicPickerOpen] = useState(false);

  return (
    <div className="border-t border-gray-100 dark:border-graphite-border bg-gray-50 dark:bg-graphite-surface-2 px-4 py-2">
      {/* Desktop: chip row + generate button */}
      <div className="hidden md:flex flex-wrap items-center gap-2">
        {DRAFT_REPLY_HINTS.map((hint) => (
          <button
            key={hint.id}
            type="button"
            disabled={disabled}
            onClick={() => onGenerate(hint.label)}
            className="inline-flex items-center rounded-full px-3 py-1 text-xs bg-white dark:bg-graphite-surface border border-gray-200 dark:border-graphite-border text-gray-700 dark:text-graphite-muted hover:bg-blue-50 dark:hover:bg-graphite-accent/10 hover:border-blue-300 dark:hover:border-graphite-accent transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {hint.label}
          </button>
        ))}
        <button
          type="button"
          data-testid="generate-reply-desktop"
          disabled={disabled || !canGenerateWithoutTopic}
          onClick={() => onGenerate(undefined)}
          title={canGenerateWithoutTopic ? undefined : NO_CUSTOMER_MESSAGE_TOOLTIP}
          className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium bg-blue-500 text-white hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Sparkles className="w-3.5 h-3.5" />
          Generovat odpověď
        </button>
      </div>

      {/* Mobile: Témata + generate buttons */}
      <div className="flex md:hidden items-center gap-2">
        <button
          type="button"
          disabled={disabled}
          onClick={() => setTopicPickerOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-full px-4 py-2.5 text-sm border border-gray-200 dark:border-graphite-border bg-white dark:bg-graphite-surface text-gray-700 dark:text-graphite-muted hover:bg-gray-50 dark:hover:bg-white/5 transition-colors disabled:opacity-50 disabled:cursor-not-allowed min-h-[40px]"
        >
          <Tag className="w-4 h-4" />
          Témata
        </button>
        <button
          type="button"
          data-testid="generate-reply-mobile"
          disabled={disabled || !canGenerateWithoutTopic}
          onClick={() => onGenerate(undefined)}
          title={canGenerateWithoutTopic ? undefined : NO_CUSTOMER_MESSAGE_TOOLTIP}
          className="inline-flex items-center gap-1.5 rounded-full px-4 py-2.5 text-sm font-medium bg-blue-500 text-white hover:bg-blue-600 transition-colors disabled:opacity-50 disabled:cursor-not-allowed min-h-[40px]"
        >
          <Sparkles className="w-4 h-4" />
          Generovat odpověď
        </button>
      </div>

      {error && <p className="mt-1.5 text-xs text-red-600 dark:text-red-400">{error}</p>}

      <TopicPickerSheet
        isOpen={topicPickerOpen}
        onSelect={(label) => {
          setTopicPickerOpen(false);
          onGenerate(label);
        }}
        onClose={() => setTopicPickerOpen(false)}
      />
    </div>
  );
}

export default DraftReplyTriggerBar;
