import React, { useState, useEffect } from "react";
import {
  X,
  Plus,
  Trash2,
  AlertCircle,
  Loader2,
  Calendar,
  CheckCircle,
  Clock,
  AlertTriangle,
} from "lucide-react";
import { format } from "date-fns";
import { cs } from "date-fns/locale";
import {
  ManufactureDifficultySettingDto,
  CreateManufactureDifficultyRequest,
} from "../api/generated/api-client";
import {
  useManufactureDifficultySettings,
  useCreateManufactureDifficulty,
  useDeleteManufactureDifficulty,
} from "../api/hooks/useManufactureDifficulty";

interface ManufactureDifficultyModalProps {
  isOpen: boolean;
  onClose: () => void;
  productCode: string;
  productName: string;
  currentDifficulty?: number;
}

interface FormData {
  difficultyValue: number;
  validFrom: string;
  validTo: string;
}

const ManufactureDifficultyModal: React.FC<ManufactureDifficultyModalProps> = ({
  isOpen,
  onClose,
  productCode,
  productName,
  currentDifficulty,
}) => {
  const [showForm, setShowForm] = useState(false);
  const [formData, setFormData] = useState<FormData>({
    difficultyValue: 1,
    validFrom: "",
    validTo: "",
  });
  const [formErrors, setFormErrors] = useState<{ [key: string]: boolean }>({});

  // API hooks
  const { data, isLoading, error } = useManufactureDifficultySettings(
    productCode,
    isOpen,
  );
  const createMutation = useCreateManufactureDifficulty();
  const deleteMutation = useDeleteManufactureDifficulty();

  // Reset form when modal opens
  useEffect(() => {
    if (isOpen) {
      setShowForm(false);
      setFormData({
        difficultyValue: 1,
        validFrom: "",
        validTo: "",
      });
      setFormErrors({});
    }
  }, [isOpen]);

  // Keyboard event listener for Esc key
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && isOpen) {
        if (showForm) {
          setShowForm(false);
        } else {
          onClose();
        }
      }
    };

    if (isOpen) {
      document.addEventListener("keydown", handleKeyDown);
    }

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, showForm, onClose]);

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      if (showForm) {
        setShowForm(false);
      } else {
        onClose();
      }
    }
  };

  const validateForm = (): boolean => {
    const errors: { [key: string]: boolean } = {};

    if (formData.difficultyValue <= 0) {
      errors.difficultyValue = true;
    }

    if (formData.validFrom && formData.validTo) {
      const fromDate = new Date(formData.validFrom);
      const toDate = new Date(formData.validTo);
      if (fromDate >= toDate) {
        errors.validFrom = true;
        errors.validTo = true;
      }
    }

    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!validateForm()) {
      return;
    }

    try {
      // Create new setting
      const request = new CreateManufactureDifficultyRequest({
        productCode: productCode,
        difficultyValue: formData.difficultyValue,
        validFrom: formData.validFrom
          ? new Date(formData.validFrom)
          : undefined,
        validTo: formData.validTo ? new Date(formData.validTo) : undefined,
      });

      await createMutation.mutateAsync(request);

      // Reset form
      setShowForm(false);
      setFormData({
        difficultyValue: 1,
        validFrom: "",
        validTo: "",
      });
      setFormErrors({});
    } catch (error) {
      console.error("Error saving manufacture difficulty:", error);
    }
  };

  const handleDelete = async (item: ManufactureDifficultySettingDto) => {
    if (
      window.confirm("Opravdu chcete smazat toto nastavení náročnosti výroby?")
    ) {
      try {
        await deleteMutation.mutateAsync({ id: item.id!, productCode });
      } catch (error) {
        console.error("Error deleting manufacture difficulty:", error);
      }
    }
  };

  const handleAdd = () => {
    const today = new Date().toISOString().split("T")[0];
    setFormData({
      difficultyValue: 1,
      validFrom: today,
      validTo: "",
    });
    setFormErrors({});
    setShowForm(true);
  };

  const formatDate = (date: Date | string | undefined) => {
    if (!date) return "-";
    return format(new Date(date), "dd.MM.yyyy", { locale: cs });
  };

  const getStatusIcon = (item: ManufactureDifficultySettingDto) => {
    if (item.isCurrent) {
      return (
        <div title="Aktuálně platné">
          <CheckCircle className="h-4 w-4 text-green-600" />
        </div>
      );
    }

    const now = new Date();
    const validFrom = item.validFrom ? new Date(item.validFrom) : null;
    const validTo = item.validTo ? new Date(item.validTo) : null;

    if (validFrom && validFrom > now) {
      return (
        <div title="Platné v budoucnosti">
          <Clock className="h-4 w-4 text-blue-600" />
        </div>
      );
    }

    if (validTo && validTo < now) {
      return (
        <div title="Již neplatné">
          <AlertTriangle className="h-4 w-4 text-gray-400" />
        </div>
      );
    }

    return null;
  };

  const sortedSettings = data?.settings
    ? [...data.settings].sort((a, b) => {
        // Current setting first
        if (a.isCurrent && !b.isCurrent) return -1;
        if (!a.isCurrent && b.isCurrent) return 1;

        // Then by validFrom date (newest first)
        const aDate = a.validFrom ? new Date(a.validFrom).getTime() : 0;
        const bDate = b.validFrom ? new Date(b.validFrom).getTime() : 0;
        return bDate - aDate;
      })
    : [];

  if (!isOpen) {
    return null;
  }

  return (
    <div
      className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50"
      onClick={handleBackdropClick}
    >
      <div className="bg-white rounded-lg shadow-xl max-w-4xl w-full max-h-[90vh] overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-gray-200">
          <div className="flex items-center space-x-3">
            <Calendar className="h-6 w-6 text-indigo-600" />
            <div>
              <h2 className="text-xl font-semibold text-gray-900">
                Náročnost výroby
              </h2>
              <p className="text-sm text-gray-500">
                {productName} (Kód: {productCode})
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 transition-colors"
          >
            <X className="h-6 w-6" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto max-h-[calc(90vh-140px)]">
          {isLoading ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2">
                <Loader2 className="h-5 w-5 animate-spin text-indigo-500" />
                <span className="text-gray-500">
                  Načítání nastavení náročnosti...
                </span>
              </div>
            </div>
          ) : error ? (
            <div className="flex items-center justify-center h-64">
              <div className="flex items-center space-x-2 text-red-600">
                <AlertCircle className="h-5 w-5" />
                <span>Chyba při načítání: {(error as any).message}</span>
              </div>
            </div>
          ) : (
            <>
              {/* Current difficulty summary */}
              {data?.currentSetting && (
                <div className="bg-green-50 border border-green-200 rounded-lg p-4 mb-6">
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="text-lg font-medium text-green-900 mb-1">
                        Aktuální náročnost výroby
                      </h3>
                      <p className="text-sm text-green-700">
                        Hodnota platná k dnešnímu dni
                      </p>
                    </div>
                    <div className="text-right">
                      <div className="text-3xl font-bold text-green-900">
                        {data.currentSetting.difficultyValue}
                      </div>
                      <div className="text-sm text-green-600">
                        {data.currentSetting.validFrom
                          ? `od ${formatDate(data.currentSetting.validFrom)}`
                          : "odjakživa"}
                        {data.currentSetting.validTo
                          ? ` do ${formatDate(data.currentSetting.validTo)}`
                          : " do odvolání"}
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Add Form */}
              {showForm && (
                <div className="bg-gray-50 border border-gray-200 rounded-lg p-6 mb-6">
                  <h3 className="text-lg font-medium text-gray-900 mb-4">
                    Nové nastavení náročnosti
                  </h3>

                  <form onSubmit={handleSubmit} className="space-y-4">
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Hodnota náročnosti *
                        </label>
                        <input
                          type="number"
                          step="1"
                          min="1"
                          value={formData.difficultyValue}
                          onChange={(e) =>
                            setFormData({
                              ...formData,
                              difficultyValue: parseInt(e.target.value) || 0,
                            })
                          }
                          className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                            formErrors.difficultyValue
                              ? "border-red-300"
                              : "border-gray-300"
                          }`}
                          required
                        />
                        {formErrors.difficultyValue && (
                          <p className="text-sm text-red-600 mt-1">
                            Hodnota musí být celé číslo větší než 0
                          </p>
                        )}
                      </div>

                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Platné od
                        </label>
                        <input
                          type="date"
                          value={formData.validFrom}
                          onChange={(e) =>
                            setFormData({
                              ...formData,
                              validFrom: e.target.value,
                            })
                          }
                          className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                            formErrors.validFrom
                              ? "border-red-300"
                              : "border-gray-300"
                          }`}
                        />
                        <p className="text-xs text-gray-500 mt-1">
                          Nepovinné - nechat prázdné pro "odjakživa"
                        </p>
                      </div>

                      <div>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          Platné do
                        </label>
                        <input
                          type="date"
                          value={formData.validTo}
                          onChange={(e) =>
                            setFormData({
                              ...formData,
                              validTo: e.target.value,
                            })
                          }
                          className={`w-full px-3 py-2 border rounded-md focus:outline-none focus:ring-2 focus:ring-indigo-500 ${
                            formErrors.validTo
                              ? "border-red-300"
                              : "border-gray-300"
                          }`}
                        />
                        <p className="text-xs text-gray-500 mt-1">
                          Nepovinné - nechat prázdné pro "do odvolání"
                        </p>
                      </div>
                    </div>

                    {(formErrors.validFrom || formErrors.validTo) && (
                      <div className="bg-red-50 border border-red-200 rounded-md p-3">
                        <p className="text-sm text-red-600">
                          Datum "Platné od" musí být dřívější než datum "Platné
                          do"
                        </p>
                      </div>
                    )}

                    <div className="flex justify-end space-x-3">
                      <button
                        type="button"
                        onClick={() => setShowForm(false)}
                        className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
                      >
                        Zrušit
                      </button>
                      <button
                        type="submit"
                        disabled={createMutation.isPending}
                        className="px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center space-x-2"
                      >
                        {createMutation.isPending && (
                          <Loader2 className="h-4 w-4 animate-spin" />
                        )}
                        <span>Vytvořit</span>
                      </button>
                    </div>
                  </form>
                </div>
              )}

              {/* Action buttons */}
              {!showForm && (
                <div className="flex justify-between items-center mb-6">
                  <h3 className="text-lg font-medium text-gray-900">
                    Historie nastavení ({sortedSettings.length})
                  </h3>
                  <button
                    onClick={handleAdd}
                    className="inline-flex items-center px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                  >
                    <Plus className="h-4 w-4 mr-2" />
                    Přidat nastavení
                  </button>
                </div>
              )}

              {/* Settings list */}
              {!showForm && (
                <>
                  {sortedSettings.length === 0 ? (
                    <div className="text-center py-12 bg-gray-50 rounded-lg">
                      <Calendar className="h-12 w-12 mx-auto mb-3 text-gray-300" />
                      <p className="text-gray-500 mb-4">
                        Žádná nastavení náročnosti výroby
                      </p>
                      <button
                        onClick={handleAdd}
                        className="inline-flex items-center px-4 py-2 text-sm font-medium text-indigo-700 bg-white border border-indigo-300 rounded-md hover:bg-indigo-50"
                      >
                        <Plus className="h-4 w-4 mr-2" />
                        Vytvořit první nastavení
                      </button>
                    </div>
                  ) : (
                    <div className="space-y-3">
                      {sortedSettings.map((setting) => (
                        <div
                          key={setting.id}
                          className={`bg-white border rounded-lg p-4 ${
                            setting.isCurrent
                              ? "border-green-300 bg-green-50"
                              : "border-gray-200 hover:shadow-sm"
                          }`}
                        >
                          <div className="flex items-center justify-between">
                            <div className="flex items-center space-x-4">
                              <div className="flex items-center space-x-2">
                                {getStatusIcon(setting)}
                                <div className="text-2xl font-bold text-gray-900">
                                  {setting.difficultyValue}
                                </div>
                              </div>

                              <div className="text-sm text-gray-600">
                                <div>
                                  <strong>Platnost:</strong>
                                  {setting.validFrom
                                    ? ` od ${formatDate(setting.validFrom)}`
                                    : " odjakživa"}
                                  {setting.validTo
                                    ? ` do ${formatDate(setting.validTo)}`
                                    : " do odvolání"}
                                </div>
                                <div className="text-xs text-gray-500 mt-1">
                                  Vytvořeno: {formatDate(setting.createdAt)}
                                  {setting.createdBy &&
                                    ` (${setting.createdBy})`}
                                </div>
                              </div>
                            </div>

                            <div className="flex space-x-2">
                              <button
                                onClick={() => handleDelete(setting)}
                                disabled={deleteMutation.isPending}
                                className="p-2 text-gray-400 hover:text-red-600 rounded-md hover:bg-red-50 disabled:opacity-50"
                                title="Smazat"
                              >
                                <Trash2 className="h-4 w-4" />
                              </button>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </>
              )}
            </>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end p-6 border-t border-gray-200 bg-gray-50">
          <button
            onClick={onClose}
            className="bg-gray-600 hover:bg-gray-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200"
          >
            Zavřít
          </button>
        </div>
      </div>
    </div>
  );
};

export default ManufactureDifficultyModal;
