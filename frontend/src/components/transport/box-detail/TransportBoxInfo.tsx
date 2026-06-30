import React from "react";
import { Box, Calendar, MapPin } from "lucide-react";
import { TransportBoxInfoProps } from "./TransportBoxTypes";
import TransportBoxStateBadge from "./components/TransportBoxStateBadge";

const TransportBoxInfo: React.FC<TransportBoxInfoProps> = ({
  transportBox,
  descriptionInput,
  handleDescriptionChange,
  isDescriptionChanged,
  isFormEditable,
  formatDate,
  handleSaveNote,
}) => {
  return (
    <div className="bg-gray-50 p-3 rounded-lg dark:bg-graphite-surface-2">
      <h3 className="text-base font-medium text-gray-900 mb-3 flex items-center gap-2 dark:text-graphite-text">
        <Box className="h-4 w-4" />
        Základní informace
      </h3>
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6 gap-3 text-sm">
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">ID</label>
          <p className="mt-0.5 text-sm text-gray-900 font-medium dark:text-graphite-text">
            {transportBox.id}
          </p>
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">Kód</label>
          <p className="mt-0.5 text-sm text-gray-900 font-medium dark:text-graphite-text">
            {transportBox.code || "-"}
          </p>
        </div>
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">
            Stav
          </label>
          <div className="mt-0.5">
            <TransportBoxStateBadge
              state={transportBox.state || ""}
              size="sm"
            />
          </div>
        </div>
        {/* Location - only show in Reserve state */}
        {transportBox.state === "Reserve" && (
          <div>
            <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">
              Lokace
            </label>
            <p className="mt-0.5 text-sm text-gray-900 flex items-center gap-1 dark:text-graphite-text">
              <MapPin className="h-3 w-3 text-gray-400 dark:text-graphite-faint" />
              {transportBox.location || "-"}
            </p>
          </div>
        )}
        <div>
          <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">
            Položky
          </label>
          <p className="mt-0.5 text-sm text-gray-900 font-medium dark:text-graphite-text">
            {transportBox.itemCount}
          </p>
        </div>
        <div className="lg:col-span-2">
          <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">
            Změna
          </label>
          <p className="mt-0.5 text-xs text-gray-900 flex items-center gap-1 dark:text-graphite-text">
            <Calendar className="h-3 w-3 text-gray-400 flex-shrink-0 dark:text-graphite-faint" />
            <span className="break-all">
              {formatDate(transportBox.lastStateChanged)}
            </span>
          </p>
        </div>
      </div>

      {/* Notes/Description Section */}
      <div className="mt-3 border-t border-gray-200 pt-3 dark:border-graphite-border">
        <div className="flex justify-between items-center mb-1">
          <label className="block text-xs font-medium text-gray-600 dark:text-graphite-muted">
            Poznámka k boxu
          </label>
          {isDescriptionChanged && (
            <button
              onClick={handleSaveNote}
              className="px-2 py-1 text-xs font-medium text-white bg-indigo-600 border border-transparent rounded hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2"
            >
              Uložit poznámku
            </button>
          )}
        </div>
        {isFormEditable("notes") ? (
          <>
            <textarea
              rows={2}
              value={descriptionInput}
              onChange={(e) => handleDescriptionChange(e.target.value)}
              placeholder="Zadejte poznámku..."
              className="w-full px-2 py-1.5 text-sm border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-transparent resize-none dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
            />
            {isDescriptionChanged && (
              <p className="mt-1 text-xs text-orange-600 dark:text-orange-400">Neuložené změny</p>
            )}
          </>
        ) : (
          <p className="text-sm text-gray-900 bg-white rounded px-2 py-1.5 border border-gray-200 min-h-[2.5rem] flex items-center dark:text-graphite-text dark:bg-graphite-surface dark:border-graphite-border">
            {transportBox.description || (
              <span className="text-gray-400 italic dark:text-graphite-faint">Žádná poznámka</span>
            )}
          </p>
        )}
      </div>
    </div>
  );
};

export default TransportBoxInfo;
