import React from "react";
import {
  User,
  Hash,
  Calendar,
  AlertCircle,
  CalendarClock,
  StickyNote,
  Maximize2,
} from "lucide-react";
import ResponsiblePersonCombobox from "../../common/ResponsiblePersonCombobox";
import { useManufactureSettingsQuery } from "../../../api/hooks/useManufactureSettings";
import { ManufactureType } from "../../../api/generated/api-client";

interface BasicInfoSectionProps {
  order: any;
  canEditFields: boolean;
  editableResponsiblePerson: string;
  editablePlannedDate: string;
  editableLotNumber: string;
  editableExpirationDate: string;
  editableManualActionRequired: boolean;
  onResponsiblePersonChange: (value: string | null) => void;
  onPlannedDateChange: (value: string) => void;
  onLotNumberChange: (value: string) => void;
  onExpirationDateChange: (value: string) => void;
  onManualActionRequiredChange: (value: boolean) => void;
  onResolveManualAction: () => void;
  onExpandNote: (noteText: string) => void;
  formatDateTime: (date: Date | string | undefined) => string;
  formatDate: (date: Date | string | undefined) => string;
  shouldTruncateText: (text: string) => boolean;
  truncateText: (text: string) => string;
}

export const BasicInfoSection: React.FC<BasicInfoSectionProps> = ({
  order,
  canEditFields,
  editableResponsiblePerson,
  editablePlannedDate,
  editableLotNumber,
  editableExpirationDate,
  editableManualActionRequired,
  onResponsiblePersonChange,
  onPlannedDateChange,
  onLotNumberChange,
  onExpirationDateChange,
  onManualActionRequiredChange,
  onResolveManualAction,
  onExpandNote,
  formatDateTime,
  formatDate,
  shouldTruncateText,
  truncateText,
}) => {
  const { data: manufactureSettings } = useManufactureSettingsQuery();
  const manufactureGroupId = manufactureSettings?.manufactureGroupId ?? "";

  const flexiDocs = [
    { label: 'Výdej materiálu (polotovar)', value: order.docMaterialIssueForSemiProduct, date: order.docMaterialIssueForSemiProductDate, hideForSinglePhase: true },
    { label: 'Příjem polotovaru', value: order.docSemiProductReceipt, date: order.docSemiProductReceiptDate, hideForSinglePhase: true },
    { label: 'Výdej polotovaru (výrobek)', value: order.docSemiProductIssueForProduct, date: order.docSemiProductIssueForProductDate, hideForSinglePhase: true },
    { label: 'Výdej materiálu (výrobek)', value: order.docMaterialIssueForProduct, date: order.docMaterialIssueForProductDate, hideForSinglePhase: false },
    { label: 'Příjem výrobku', value: order.docProductReceipt, date: order.docProductReceiptDate, hideForSinglePhase: false },
  ];
  return (
    <div className="bg-gray-50 dark:bg-graphite-surface-2 rounded-lg p-3">
      <h3 className="text-base font-semibold text-gray-800 dark:text-graphite-muted mb-3 flex items-center">
        <User className="h-4 w-4 mr-2 text-indigo-600 dark:text-graphite-accent" />
        Základní informace
      </h3>
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <User className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
            <span className="text-sm text-gray-500 dark:text-graphite-muted">Odpovědná osoba:</span>
          </div>
          {canEditFields ? (
            <div className="w-48">
              <ResponsiblePersonCombobox
                groupId={manufactureGroupId}
                value={editableResponsiblePerson}
                onChange={(value) => onResponsiblePersonChange(value)}
                placeholder="Vyberte..."
                allowManualEntry={true}
              />
            </div>
          ) : (
            <span className="text-sm text-gray-900 dark:text-graphite-text">
              {order.responsiblePerson || "Není přiřazena"}
            </span>
          )}
        </div>
        
        {/* Flexi document numbers (read-only, captured from submission pipeline) */}
        {flexiDocs
          .filter(doc => !(doc.hideForSinglePhase && order?.manufactureType === ManufactureType.SinglePhase))
          .map(doc => (
            <div key={doc.label} className="flex items-center justify-between">
              <div className="flex items-center">
                <Hash className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
                <span className="text-sm text-gray-500 dark:text-graphite-muted">{doc.label}:</span>
              </div>
              <span
                className="text-sm text-gray-900 dark:text-graphite-text"
                title={doc.date ? `Datum: ${formatDateTime(doc.date)}` : undefined}
              >
                {doc.value || "-"}
              </span>
            </div>
          ))
        }
        
        
        {/* Manual Action Required Section */}
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <AlertCircle className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
            <span className="text-sm text-gray-500 dark:text-graphite-muted">Vyžaduje ruční zásah:</span>
          </div>
          <div className="flex items-center space-x-2">
            <input
              type="checkbox"
              checked={editableManualActionRequired}
              onChange={(e) => onManualActionRequiredChange(e.target.checked)}
              className="h-4 w-4 text-indigo-600 border-gray-300 dark:border-graphite-border rounded focus:ring-indigo-500"
            />
            {editableManualActionRequired && (
              <button
                onClick={onResolveManualAction}
                className="px-3 py-1 text-xs bg-green-600 text-white rounded hover:bg-green-700 transition-colors duration-150"
              >
                Vyřešeno
              </button>
            )}
          </div>
        </div>
        
        {/* Planned Dates Section */}
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <Calendar className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm text-gray-500 dark:text-graphite-muted">Datum:</span>
            </div>
            {canEditFields ? (
              <input
                type="date"
                value={editablePlannedDate}
                onChange={(e) => onPlannedDateChange(e.target.value)}
                className="text-sm border border-gray-300 rounded px-2 py-1 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
              />
            ) : (
              <span className="text-sm text-gray-900 dark:text-graphite-text">
                {order.plannedDate ? formatDate(order.plannedDate) : "-"}
              </span>
            )}
          </div>
        </div>
        <div className="mt-3 pt-3 border-t border-gray-200 dark:border-graphite-border">
          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <Hash className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm text-gray-500 dark:text-graphite-muted">Šarže:</span>
            </div>
            {canEditFields ? (
              <input
                type="text"
                value={editableLotNumber}
                onChange={(e) => onLotNumberChange(e.target.value)}
                className="text-sm border border-gray-300 rounded px-2 py-1 w-28 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
                placeholder="38202412"
              />
            ) : (
              <span className="text-sm text-gray-900 dark:text-graphite-text">
                {order.semiProduct?.lotNumber || "-"}
              </span>
            )}
          </div>
          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <CalendarClock className="h-4 w-4 text-gray-400 dark:text-graphite-faint mr-2" />
              <span className="text-sm text-gray-500 dark:text-graphite-muted">Expirace ({order.semiProduct?.expirationMonths} měsíců):</span>
            </div>
            {canEditFields ? (
              <input
                type="month"
                lang="cs"
                value={editableExpirationDate ? editableExpirationDate.substring(0, 7) : ""}
                onChange={(e) => onExpirationDateChange(e.target.value + "-01")}
                className="text-sm border border-gray-300 rounded px-2 py-1 dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint"
              />
            ) : (
              <span className="text-sm text-gray-900 dark:text-graphite-text">
                {order.semiProduct?.expirationDate ? formatDate(order.semiProduct.expirationDate) : "-"}
              </span>
            )}
          </div>
        </div>

        {/* Latest Note */}
        <div className="mt-3 pt-3 border-t border-gray-200 dark:border-graphite-border">
          <h4 className="text-sm font-medium text-gray-700 dark:text-graphite-muted mb-2">Poslední poznámka:</h4>
          {order.notes && order.notes.length > 0 ? (
            <div className="bg-yellow-50 border border-yellow-200 dark:bg-amber-900/20 dark:border-amber-900/40 rounded-lg p-3 relative">
              <div className="flex items-start space-x-2">
                <div className="p-1 bg-yellow-100 dark:bg-amber-900/30 rounded-full mt-1">
                  <StickyNote className="h-3 w-3 text-yellow-600 dark:text-amber-400" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between mb-1">
                    <span className="text-sm font-medium text-gray-900 dark:text-graphite-text">
                      {order.notes[0].createdByUser || "Neznámý"}
                    </span>
                    <span className="text-xs text-gray-500 dark:text-graphite-muted">
                      {formatDateTime(order.notes[0].createdAt)}
                    </span>
                  </div>
                  <div className="relative">
                    <p className="text-sm text-gray-800 dark:text-graphite-muted whitespace-pre-wrap" style={{
                      display: '-webkit-box', 
                      WebkitLineClamp: 2, 
                      WebkitBoxOrient: 'vertical', 
                      overflow: 'hidden' 
                    }}>
                      {order.notes && order.notes[0]?.text && shouldTruncateText(order.notes[0].text) 
                        ? truncateText(order.notes[0].text)
                        : (order.notes && order.notes[0]?.text) || ''}
                    </p>
                    {order.notes && order.notes[0]?.text && shouldTruncateText(order.notes[0].text) && (
                      <button
                        onClick={() => onExpandNote(order.notes![0].text || '')}
                        className="absolute top-0 right-0 p-1 bg-yellow-100 hover:bg-yellow-200 dark:bg-amber-900/30 dark:hover:bg-amber-900/50 rounded-full transition-colors"
                        title="Rozbalit poznámku"
                      >
                        <Maximize2 className="h-3 w-3 text-yellow-600 dark:text-amber-400" />
                      </button>
                    )}
                  </div>
                </div>
              </div>
            </div>
          ) : (
            <p className="text-gray-500 dark:text-graphite-muted text-sm italic">
              Zatím nejsou žádné poznámky
            </p>
          )}
        </div>
      </div>
    </div>
  );
};

export default BasicInfoSection;