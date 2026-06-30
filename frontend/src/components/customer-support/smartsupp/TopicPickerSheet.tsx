import React from "react";
import { DRAFT_REPLY_HINTS } from "./draftReplyHints";

interface TopicPickerSheetProps {
  isOpen: boolean;
  onSelect: (label: string) => void;
  onClose: () => void;
}

const TopicPickerSheet: React.FC<TopicPickerSheetProps> = ({ isOpen, onSelect, onClose }) => {
  if (!isOpen) return null;

  return (
    <div
      data-testid="topic-picker-backdrop"
      className="fixed inset-0 z-50 flex items-end bg-black/40"
      onClick={onClose}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Vyberte téma"
        className="bg-white dark:bg-graphite-surface rounded-t-2xl w-full"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-4 py-3 border-b border-gray-200 dark:border-graphite-border">
          <p className="font-semibold text-gray-900 dark:text-graphite-text">Vyberte téma</p>
        </div>
        <ul>
          {DRAFT_REPLY_HINTS.map((hint) => (
            <li key={hint.id}>
              <button
                type="button"
                onClick={() => onSelect(hint.label)}
                className="w-full text-left px-4 py-3 text-sm text-gray-800 dark:text-graphite-text hover:bg-gray-50 dark:hover:bg-white/5 min-h-[44px] flex items-center"
              >
                {hint.label}
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
};

export default TopicPickerSheet;
