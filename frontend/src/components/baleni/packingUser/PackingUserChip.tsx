import { UserRound } from "lucide-react";
import { usePackingUser } from "./PackingUserContext";

export function PackingUserChip() {
  const { current, openPicker } = usePackingUser();
  return (
    <button
      onClick={openPicker}
      className="inline-flex items-center gap-2 rounded-full border border-border-light dark:border-graphite-border px-3 py-1.5 text-sm font-medium text-neutral-slate dark:text-graphite-text hover:bg-secondary-blue-pale dark:hover:bg-graphite-surface-2"
      aria-label="Změnit balícího uživatele"
    >
      <UserRound className="h-4 w-4 text-primary-blue dark:text-graphite-accent" />
      <span>{current ? current.displayName : "Vybrat baliče"}</span>
    </button>
  );
}
