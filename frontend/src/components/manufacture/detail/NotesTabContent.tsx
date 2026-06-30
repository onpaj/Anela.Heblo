import React from "react";
import { StickyNote } from "lucide-react";

interface NotesTabContentProps {
  order: any;
  newNote: string;
  onNewNoteChange: (value: string) => void;
  formatDateTime: (date: Date | string | undefined) => string;
}

export const NotesTabContent: React.FC<NotesTabContentProps> = ({
  order,
  newNote,
  onNewNoteChange,
  formatDateTime,
}) => {
  return (
    <div>
      <h3 className="text-lg font-medium text-gray-900 dark:text-graphite-text mb-4 flex items-center">
        <StickyNote className="h-5 w-5 mr-2 text-indigo-600 dark:text-graphite-accent" />
        Poznámky
      </h3>
      
      {/* Add New Note */}
      <div className="mb-6 bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-4">
        <label htmlFor="newNote" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2">
          Přidat poznámku:
        </label>
        <textarea
          id="newNote"
          value={newNote}
          onChange={(e) => onNewNoteChange(e.target.value)}
          placeholder="Napište poznámku..."
          className="w-full h-20 px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 focus:border-indigo-500 resize-none text-sm dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
          rows={3}
        />
      </div>

      {order.notes && order.notes.length > 0 ? (
        <div className="space-y-4">
          {order.notes.map((note: any, index: number) => (
            <div key={index} className="bg-yellow-50 border border-yellow-200 dark:bg-amber-900/20 dark:border-amber-900/40 rounded-lg p-4">
              <div className="flex items-start space-x-3">
                <StickyNote className="h-5 w-5 text-yellow-600 dark:text-amber-400 mt-0.5" />
                <div className="flex-1">
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                      {note.createdByUser || "Neznámý"}
                    </span>
                    <span className="text-xs text-gray-500 dark:text-graphite-muted">
                      {formatDateTime(note.createdAt)}
                    </span>
                  </div>
                  <p className="text-sm text-gray-900 dark:text-graphite-text whitespace-pre-wrap">
                    {note.text}
                  </p>
                </div>
              </div>
            </div>
          ))}
        </div>
      ) : (
        <p className="text-gray-500 dark:text-graphite-muted text-center py-8">Žádné poznámky nebyly přidány.</p>
      )}
    </div>
  );
};

export default NotesTabContent;