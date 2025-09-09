import React from "react";
import { useParams, Navigate } from "react-router-dom";
import { AlertCircle } from "lucide-react";
import JournalEntryForm from "../JournalEntryForm";
import { useJournalEntry } from "../../api/hooks/useJournal";

export default function JournalEntryEdit() {
  const { id } = useParams<{ id: string }>();
  const entryId = id ? parseInt(id, 10) : 0;

  const { data, isLoading, error } = useJournalEntry(entryId);

  // Redirect if invalid ID
  if (!id || isNaN(entryId) || entryId <= 0) {
    return <Navigate to="/journal" replace />;
  }

  // Loading state
  if (isLoading) {
    return (
      <div className="min-h-screen bg-gray-50">
        <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          <div className="max-w-4xl mx-auto">
            <div className="bg-white shadow-sm border border-gray-200 rounded-lg p-6">
              <div className="animate-pulse">
                <div className="h-6 bg-gray-300 rounded mb-4 w-1/3"></div>
                <div className="h-4 bg-gray-300 rounded mb-6 w-2/3"></div>
                <div className="space-y-4">
                  <div className="h-10 bg-gray-300 rounded"></div>
                  <div className="h-10 bg-gray-300 rounded"></div>
                  <div className="h-32 bg-gray-300 rounded"></div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Error state
  if (error) {
    return (
      <div className="min-h-screen bg-gray-50">
        <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          <div className="max-w-4xl mx-auto">
            <div className="bg-white shadow-sm border border-gray-200 rounded-lg p-6">
              <div className="flex items-center text-red-600">
                <AlertCircle className="h-5 w-5 mr-2" />
                <span className="font-medium">Chyba při načítání záznamu</span>
              </div>
              <p className="mt-2 text-sm text-gray-600">
                Záznam s ID {entryId} se nepodařilo načíst. Možná byl smazán
                nebo nemáte oprávnění k jeho zobrazení.
              </p>
              <button
                onClick={() => window.history.back()}
                className="mt-4 inline-flex items-center px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
              >
                Zpět
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Entry not found
  if (!data) {
    return (
      <div className="min-h-screen bg-gray-50">
        <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
          <div className="max-w-4xl mx-auto">
            <div className="bg-white shadow-sm border border-gray-200 rounded-lg p-6">
              <div className="text-center">
                <AlertCircle className="mx-auto h-12 w-12 text-gray-400" />
                <h3 className="mt-2 text-sm font-medium text-gray-900">
                  Záznam nenalezen
                </h3>
                <p className="mt-1 text-sm text-gray-500">
                  Záznam s ID {entryId} neexistuje.
                </p>
                <button
                  onClick={() => window.history.back()}
                  className="mt-4 inline-flex items-center px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
                >
                  Zpět na seznam
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-50">
      <div className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        <JournalEntryForm entry={data} isEdit={true} />
      </div>
    </div>
  );
}
