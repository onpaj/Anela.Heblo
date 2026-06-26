import { MessageSquare, StickyNote } from 'lucide-react';

interface PackingOrderNotesProps {
  customerNote: string | null;
  eshopNote: string | null;
}

interface NoteCardProps {
  icon: typeof MessageSquare;
  label: string;
  text: string;
  className: string;
  iconClassName: string;
}

function NoteCard({ icon: Icon, label, text, className, iconClassName }: NoteCardProps) {
  return (
    <div className={`flex gap-2 rounded-lg border px-3 py-2 ${className}`}>
      <Icon className={`h-4 w-4 shrink-0 mt-0.5 ${iconClassName}`} />
      <div className="min-w-0">
        <p className="text-xs font-semibold uppercase tracking-wide">{label}</p>
        <p className="text-sm text-neutral-slate dark:text-graphite-text whitespace-pre-line">{text}</p>
      </div>
    </div>
  );
}

/** Renders the customer and internal notes for an order. Renders nothing when both are empty. */
function PackingOrderNotes({ customerNote, eshopNote }: PackingOrderNotesProps) {
  if (!customerNote && !eshopNote) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2" data-testid="packing-order-notes">
      {customerNote && (
        <NoteCard
          icon={MessageSquare}
          label="Poznámka zákazníka"
          text={customerNote}
          className="border-primary-blue dark:border-graphite-accent bg-secondary-blue-pale dark:bg-graphite-surface-2 text-primary-blue dark:text-graphite-accent"
          iconClassName="text-primary-blue dark:text-graphite-accent"
        />
      )}
      {eshopNote && (
        <NoteCard
          icon={StickyNote}
          label="Interní poznámka"
          text={eshopNote}
          className="border-border-light dark:border-graphite-border bg-white dark:bg-graphite-surface text-neutral-gray dark:text-graphite-muted"
          iconClassName="text-neutral-gray dark:text-graphite-muted"
        />
      )}
    </div>
  );
}

export default PackingOrderNotes;
