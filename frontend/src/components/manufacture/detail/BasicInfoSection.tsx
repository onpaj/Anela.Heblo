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

interface BasicInfoSectionProps {
  order: any;
  canEditFields: boolean;
  editableResponsiblePerson: string;
  editableErpOrderNumberSemiproduct: string;
  editableErpOrderNumberProduct: string;
  editableErpDiscardResidueDocumentNumber: string;
  editablePlannedDate: string;
  editableLotNumber: string;
  editableExpirationDate: string;
  editableManualActionRequired: boolean;
  onResponsiblePersonChange: (value: string | null) => void;
  onErpOrderNumberSemiproductChange: (value: string) => void;
  onErpOrderNumberProductChange: (value: string) => void;
  onErpDiscardResidueDocumentNumberChange: (value: string) => void;
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
  editableErpOrderNumberSemiproduct,
  editableErpOrderNumberProduct,
  editableErpDiscardResidueDocumentNumber,
  editablePlannedDate,
  editableLotNumber,
  editableExpirationDate,
  editableManualActionRequired,
  onResponsiblePersonChange,
  onErpOrderNumberSemiproductChange,
  onErpOrderNumberProductChange,
  onErpDiscardResidueDocumentNumberChange,
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
  return (
    <div className="bg-gray-50 rounded-lg p-3">
      <h3 className="text-base font-semibold text-gray-800 mb-3 flex items-center">
        <User className="h-4 w-4 mr-2 text-indigo-600" />
        Základní informace
      </h3>
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <User className="h-4 w-4 text-gray-400 mr-2" />
            <span className="text-sm text-gray-500">Odpovědná osoba:</span>
          </div>
          {canEditFields ? (
            <div className="w-48">
              <ResponsiblePersonCombobox
                value={editableResponsiblePerson}
                onChange={(value) => onResponsiblePersonChange(value)}
                placeholder="Vyberte..."
                allowManualEntry={true}
              />
            </div>
          ) : (
            <span className="text-sm text-gray-900">
              {order.responsiblePerson || "Není přiřazena"}
            </span>
          )}
        </div>
        
        {/* ERP Order Numbers */}
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <Hash className="h-4 w-4 text-gray-400 mr-2" />
            <span className="text-sm text-gray-500">ERP č. (meziprod.):</span>
          </div>
          {canEditFields ? (
            <input
              type="text"
              value={editableErpOrderNumberSemiproduct}
              onChange={(e) => onErpOrderNumberSemiproductChange(e.target.value)}
              className="w-48 text-sm border border-gray-300 rounded px-2 py-1"
              placeholder="ERP číslo pro meziprodukt"
              title={order.erpOrderNumberSemiproductDate ? `Datum: ${formatDateTime(order.erpOrderNumberSemiproductDate)}` : "Datum není nastaveno"}
            />
          ) : (
            <span 
              className="text-sm text-gray-900"
              title={order.erpOrderNumberSemiproductDate ? `Datum: ${formatDateTime(order.erpOrderNumberSemiproductDate)}` : "Datum není nastaveno"}
            >
              {order.erpOrderNumberSemiproduct || "-"}
            </span>
          )}
        </div>
        
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <Hash className="h-4 w-4 text-gray-400 mr-2" />
            <span className="text-sm text-gray-500">ERP č. (produkt):</span>
          </div>
          {canEditFields ? (
            <input
              type="text"
              value={editableErpOrderNumberProduct}
              onChange={(e) => onErpOrderNumberProductChange(e.target.value)}
              className="w-48 text-sm border border-gray-300 rounded px-2 py-1"
              placeholder="ERP číslo pro produkt"
              title={order.erpOrderNumberProductDate ? `Datum: ${formatDateTime(order.erpOrderNumberProductDate)}` : "Datum není nastaveno"}
            />
          ) : (
            <span 
              className="text-sm text-gray-900"
              title={order.erpOrderNumberProductDate ? `Datum: ${formatDateTime(order.erpOrderNumberProductDate)}` : "Datum není nastaveno"}
            >
              {order.erpOrderNumberProduct || "-"}
            </span>
          )}
        </div>
        
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <Hash className="h-4 w-4 text-gray-400 mr-2" />
            <span className="text-sm text-gray-500">ERP vydejka zbytku:</span>
          </div>
          {canEditFields ? (
            <input
              type="text"
              value={editableErpDiscardResidueDocumentNumber}
              onChange={(e) => onErpDiscardResidueDocumentNumberChange(e.target.value)}
              className="w-48 text-sm border border-gray-300 rounded px-2 py-1"
              placeholder="ERP číslo pro vydejku zbytku"
              title={order.erpDiscardResidueDocumentNumberDate ? `Datum: ${formatDateTime(order.erpDiscardResidueDocumentNumberDate)}` : "Datum není nastaveno"}
            />
          ) : (
            <span 
              className="text-sm text-gray-900"
              title={order.erpDiscardResidueDocumentNumberDate ? `Datum: ${formatDateTime(order.erpDiscardResidueDocumentNumberDate)}` : "Datum není nastaveno"}
            >
              {order.erpDiscardResidueDocumentNumber || "-"}
            </span>
          )}
        </div>
        
        {/* Manual Action Required Section */}
        <div className="flex items-center justify-between">
          <div className="flex items-center">
            <AlertCircle className="h-4 w-4 text-gray-400 mr-2" />
            <span className="text-sm text-gray-500">Vyžaduje ruční zásah:</span>
          </div>
          <div className="flex items-center space-x-2">
            <input
              type="checkbox"
              checked={editableManualActionRequired}
              onChange={(e) => onManualActionRequiredChange(e.target.checked)}
              className="h-4 w-4 text-indigo-600 border-gray-300 rounded focus:ring-indigo-500"
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
              <Calendar className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm text-gray-500">Datum:</span>
            </div>
            {canEditFields ? (
              <input
                type="date"
                value={editablePlannedDate}
                onChange={(e) => onPlannedDateChange(e.target.value)}
                className="text-sm border border-gray-300 rounded px-2 py-1"
              />
            ) : (
              <span className="text-sm text-gray-900">
                {order.plannedDate ? formatDate(order.plannedDate) : "-"}
              </span>
            )}
          </div>
        </div>
        <div className="mt-3 pt-3 border-t border-gray-200">
          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <Hash className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm text-gray-500">Šarže:</span>
            </div>
            {canEditFields ? (
              <input
                type="text"
                value={editableLotNumber}
                onChange={(e) => onLotNumberChange(e.target.value)}
                className="text-sm border border-gray-300 rounded px-2 py-1 w-28"
                placeholder="38202412"
              />
            ) : (
              <span className="text-sm text-gray-900">
                {order.semiProduct?.lotNumber || "-"}
              </span>
            )}
          </div>
          <div className="flex items-center justify-between">
            <div className="flex items-center">
              <CalendarClock className="h-4 w-4 text-gray-400 mr-2" />
              <span className="text-sm text-gray-500">Expirace ({order.semiProduct?.expirationMonths} měsíců):</span>
            </div>
            {canEditFields ? (
              <input
                type="month"
                lang="cs"
                value={editableExpirationDate ? editableExpirationDate.substring(0, 7) : ""}
                onChange={(e) => onExpirationDateChange(e.target.value + "-01")}
                className="text-sm border border-gray-300 rounded px-2 py-1"
              />
            ) : (
              <span className="text-sm text-gray-900">
                {order.semiProduct?.expirationDate ? formatDate(order.semiProduct.expirationDate) : "-"}
              </span>
            )}
          </div>
        </div>

        {/* Latest Note */}
        <div className="mt-3 pt-3 border-t border-gray-200">
          <h4 className="text-sm font-medium text-gray-700 mb-2">Poslední poznámka:</h4>
          {order.notes && order.notes.length > 0 ? (
            <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-3 relative">
              <div className="flex items-start space-x-2">
                <div className="p-1 bg-yellow-100 rounded-full mt-1">
                  <StickyNote className="h-3 w-3 text-yellow-600" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between mb-1">
                    <span className="text-sm font-medium text-gray-900">
                      {order.notes[0].createdByUser || "Neznámý"}
                    </span>
                    <span className="text-xs text-gray-500">
                      {formatDateTime(order.notes[0].createdAt)}
                    </span>
                  </div>
                  <div className="relative">
                    <p className="text-sm text-gray-800 whitespace-pre-wrap" style={{ 
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
                        className="absolute top-0 right-0 p-1 bg-yellow-100 hover:bg-yellow-200 rounded-full transition-colors"
                        title="Rozbalit poznámku"
                      >
                        <Maximize2 className="h-3 w-3 text-yellow-600" />
                      </button>
                    )}
                  </div>
                </div>
              </div>
            </div>
          ) : (
            <p className="text-gray-500 text-sm italic">
              Zatím nejsou žádné poznámky
            </p>
          )}
        </div>
      </div>
    </div>
  );
};

export default BasicInfoSection;