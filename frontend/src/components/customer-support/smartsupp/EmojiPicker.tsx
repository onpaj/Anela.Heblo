import { useEffect, useRef, useState } from "react";
import { Smile } from "lucide-react";

export const COMPOSER_EMOJI: readonly string[] = [
  "😊", "🙂", "😁", "🥰", "😉", "🙏", "👍", "🙌", "👉", "✅", "⚠️", "💡",
  "❤️", "💛", "💚", "🤍", "✨", "🌿", "🌸", "🌙", "💧", "🧴", "🎁", "📦",
];

interface EmojiPickerProps {
  onPick: (emoji: string) => void;
  disabled: boolean;
}

function EmojiPicker({ onPick, disabled }: EmojiPickerProps) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!isOpen) return;
    const handleMouseDown = (e: MouseEvent) => {
      if (!containerRef.current?.contains(e.target as Node)) setIsOpen(false);
    };
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") setIsOpen(false);
    };
    document.addEventListener("mousedown", handleMouseDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleMouseDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen]);

  const handlePick = (emoji: string) => {
    setIsOpen(false);
    onPick(emoji);
  };

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        disabled={disabled}
        aria-label="Vložit emoji"
        title="Vložit emoji"
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        className="rounded p-1 text-gray-400 dark:text-graphite-faint hover:bg-gray-100 dark:hover:bg-white/5 hover:text-gray-600 disabled:cursor-not-allowed disabled:opacity-50"
      >
        <Smile className="h-4 w-4" />
      </button>
      {isOpen && (
        <div
          role="listbox"
          aria-label="Emoji"
          className="absolute bottom-full left-0 z-10 mb-1 grid w-56 grid-cols-6 gap-1 rounded-md border border-gray-200 dark:border-graphite-border bg-white dark:bg-graphite-surface p-2 shadow-lg"
        >
          {COMPOSER_EMOJI.map((emoji) => (
            <button
              key={emoji}
              type="button"
              role="option"
              aria-selected={false}
              onClick={() => handlePick(emoji)}
              className="rounded p-1 text-lg leading-none hover:bg-gray-100 dark:hover:bg-white/5"
            >
              {emoji}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

export default EmojiPicker;
