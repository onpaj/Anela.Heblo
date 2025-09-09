import React from "react";
import {
  BookOpen,
  Plus,
  Calendar,
  Edit2,
  ExternalLink,
  Loader2,
  AlertCircle,
} from "lucide-react";
import { JournalEntryDto } from "../../../../api/generated/api-client";
import { useJournalEntriesByProduct } from "../../../../api/hooks/useJournal";
import { format } from "date-fns";

interface JournalTabProps {
  productCode: string;
  onAddEntry: () => void;
  onEditEntry: (entry: JournalEntryDto) => void;
  onViewAllEntries: () => void;
}

const JournalTab: React.FC<JournalTabProps> = ({
  productCode,
  onAddEntry,
  onEditEntry,
  onViewAllEntries,
}) => {
  const { data, isLoading, error } = useJournalEntriesByProduct(productCode);
  const entries = data?.entries || [];

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2">
          <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
          <div className="text-gray-500">Načítání záznamů deníku...</div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center space-x-2 text-red-600">
          <AlertCircle className="h-5 w-5" />
          <div>Chyba při načítání deníku: {(error as any).message}</div>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header with Add button */}
      <div className="flex items-center justify-between">
        <h3 className="text-lg font-medium text-gray-900 flex items-center">
          <BookOpen className="h-5 w-5 mr-2 text-gray-500" />
          Záznamy deníku ({entries.length})
        </h3>
        <button
          onClick={onAddEntry}
          className="inline-flex items-center px-3 py-1.5 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 transition-colors"
        >
          <Plus className="h-4 w-4 mr-1.5" />
          Přidat záznam
        </button>
      </div>

      {/* Journal entries list */}
      {entries.length === 0 ? (
        <div className="text-center py-12 bg-gray-50 rounded-lg">
          <BookOpen className="h-12 w-12 mx-auto mb-3 text-gray-300" />
          <p className="text-gray-500 mb-4">
            Žádné záznamy deníku pro tento produkt
          </p>
          <button
            onClick={onAddEntry}
            className="inline-flex items-center px-4 py-2 text-sm font-medium text-indigo-700 bg-white border border-indigo-300 rounded-md hover:bg-indigo-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
          >
            <Plus className="h-4 w-4 mr-1.5" />
            Vytvořit první záznam
          </button>
        </div>
      ) : (
        <div className="space-y-3">
          {entries.map((entry) => (
            <div
              key={entry.id}
              className="bg-white border border-gray-200 rounded-lg p-4 hover:shadow-md transition-shadow cursor-pointer"
              onClick={() => onEditEntry(entry)}
            >
              <div className="flex items-start justify-between">
                <div className="flex-1">
                  <div className="flex items-center space-x-3 mb-2">
                    <h4 className="text-sm font-semibold text-gray-900">
                      {entry.title || "Bez názvu"}
                    </h4>
                    <span className="text-xs text-gray-500 flex items-center">
                      <Calendar className="h-3 w-3 mr-1" />
                      {entry.entryDate
                        ? format(new Date(entry.entryDate), "dd.MM.yyyy")
                        : ""}
                    </span>
                  </div>
                  <p className="text-sm text-gray-600 line-clamp-2">
                    {entry.content}
                  </p>
                  {entry.tags && entry.tags.length > 0 && (
                    <div className="flex flex-wrap gap-1 mt-2">
                      {entry.tags.map((tag) => (
                        <span
                          key={tag.id}
                          className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-gray-100 text-gray-700"
                        >
                          {tag.name}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    onEditEntry(entry);
                  }}
                  className="ml-3 text-gray-400 hover:text-gray-600"
                  title="Upravit záznam"
                >
                  <Edit2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ))}

          {/* Show more button if there are many entries */}
          {entries.length >= 10 && (
            <div className="text-center pt-2">
              <button
                onClick={onViewAllEntries}
                className="inline-flex items-center text-sm text-indigo-600 hover:text-indigo-700"
              >
                <ExternalLink className="h-4 w-4 mr-1" />
                Zobrazit všechny záznamy v deníku
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default JournalTab;
