import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import {
  Calendar,
  FileText,
  Tag,
  Save,
  X,
  Plus,
  Trash2,
  Type,
  Search,
} from "lucide-react";
import { format } from "date-fns";
import {
  useCreateJournalEntry,
  useUpdateJournalEntry,
  useJournalTags,
  useCreateJournalTag,
} from "../api/hooks/useJournal";
import { CatalogAutocomplete } from "./common/CatalogAutocomplete";
import {
  catalogItemToProductCode,
  PRODUCT_TYPE_FILTERS,
} from "./common/CatalogAutocompleteAdapters";
import type { JournalEntryDto, GetJournalEntryResponse } from "../api/generated/api-client";
import {
  CreateJournalEntryRequest,
  UpdateJournalEntryRequest,
  CreateJournalTagRequest,
} from "../api/generated/api-client";

interface JournalEntryFormProps {
  entry?: GetJournalEntryResponse;
  onSave?: (entry: JournalEntryDto) => void;
  onCancel?: () => void;
  onDelete?: () => void;
  isEdit?: boolean;
}

export default function JournalEntryForm({
  entry,
  onSave,
  onCancel,
  onDelete,
  isEdit = false,
}: JournalEntryFormProps) {
  const navigate = useNavigate();
  const createMutation = useCreateJournalEntry();
  const updateMutation = useUpdateJournalEntry();
  const createTagMutation = useCreateJournalTag();
  const { data: tagsData } = useJournalTags();

  // Form state
  const [title, setTitle] = useState(entry?.entry?.title || "");
  const [content, setContent] = useState(entry?.entry?.content || "");
  const [entryDate, setEntryDate] = useState(
    entry?.entry?.entryDate
      ? format(new Date(entry.entry.entryDate), "yyyy-MM-dd")
      : format(new Date(), "yyyy-MM-dd"),
  );
  const [selectedTags, setSelectedTags] = useState<number[]>(
    entry?.entry?.tags?.map((tag) => tag.id!).filter((id) => id !== undefined) || [],
  );
  const [associatedProducts, setAssociatedProducts] = useState<string[]>(
    entry?.entry?.associatedProducts || [],
  );
  const [currentProduct, setCurrentProduct] = useState<string | null>(null);
  const [newTagName, setNewTagName] = useState("");
  const [showNewTagInput, setShowNewTagInput] = useState(false);
  const [useTextInput, setUseTextInput] = useState(false);

  // Validation state
  const [errors, setErrors] = useState<{ [key: string]: string }>({});

  // Loading state
  const isLoading =
    createMutation.isPending ||
    updateMutation.isPending ||
    createTagMutation.isPending;

  // Update form state when entry prop changes (for edit mode)
  useEffect(() => {
    console.log("🐛 JournalEntryForm useEffect - entry:", entry, "isEdit:", isEdit);
    if (entry?.entry) {
      const entryData = entry.entry;
      console.log("🐛 Updating form with entry data:", {
        title: entryData.title,
        content: entryData.content,
        entryDate: entryData.entryDate,
        tags: entryData.tags,
        products: entryData.associatedProducts
      });
      setTitle(entryData.title || "");
      setContent(entryData.content || "");
      setEntryDate(
        entryData.entryDate
          ? format(new Date(entryData.entryDate), "yyyy-MM-dd")
          : format(new Date(), "yyyy-MM-dd"),
      );
      setSelectedTags(
        entryData.tags?.map((tag) => tag.id!).filter((id) => id !== undefined) || [],
      );
      setAssociatedProducts(entryData.associatedProducts || []);
    } else if (!isEdit) {
      console.log("🐛 Resetting form for new entry");
      // Reset form for new entries
      setTitle("");
      setContent("");
      setEntryDate(format(new Date(), "yyyy-MM-dd"));
      setSelectedTags([]);
      setAssociatedProducts([]);
    }
  }, [entry, isEdit]);

  const validateForm = (): boolean => {
    const newErrors: { [key: string]: string } = {};

    if (!title.trim()) {
      newErrors.title = "Název je povinný";
    }

    if (!content.trim()) {
      newErrors.content = "Obsah je povinný";
    }

    if (!entryDate) {
      newErrors.entryDate = "Datum je povinné";
    }

    setErrors(newErrors);
    return Object.keys(newErrors).length === 0;
  };

  const handleSave = async () => {
    if (!validateForm()) return;

    try {
      if (isEdit && entry?.entry) {
        const updateRequest = new UpdateJournalEntryRequest({
          id: entry.entry.id,
          title: title.trim(),
          content: content.trim(),
          entryDate: new Date(entryDate),
          tagIds: selectedTags,
          associatedProducts: associatedProducts,
        });

        const result = await updateMutation.mutateAsync({
          id: entry.entry.id!,
          request: updateRequest,
        });

        if (onSave && result) {
          onSave(result);
        } else {
          navigate("/journal");
        }
      } else {
        const createRequest = new CreateJournalEntryRequest({
          title: title.trim(),
          content: content.trim(),
          entryDate: new Date(entryDate),
          tagIds: selectedTags,
          associatedProducts: associatedProducts,
        });

        const result = await createMutation.mutateAsync(createRequest);

        if (onSave && result) {
          onSave(result);
        } else {
          navigate("/journal");
        }
      }
    } catch (error) {
      console.error("Error saving journal entry:", error);
    }
  };

  const handleCancel = () => {
    if (onCancel) {
      onCancel();
    } else {
      navigate("/journal");
    }
  };

  const handleProductSelect = (productCode: string | null) => {
    if (
      productCode &&
      productCode.trim() &&
      !associatedProducts.includes(productCode.trim())
    ) {
      setAssociatedProducts([...associatedProducts, productCode.trim()]);
    }
    setCurrentProduct(null); // Reset for next selection
  };

  const handleRemoveProduct = (productCode: string) => {
    setAssociatedProducts(associatedProducts.filter((p) => p !== productCode));
  };

  const handleTagToggle = (tagId: number) => {
    setSelectedTags((prev) =>
      prev.includes(tagId)
        ? prev.filter((id) => id !== tagId)
        : [...prev, tagId],
    );
  };

  const handleCreateNewTag = async () => {
    if (!newTagName.trim()) return;

    try {
      const createTagRequest = new CreateJournalTagRequest({
        name: newTagName.trim(),
        color: "#6366f1", // Default indigo color
      });
      await createTagMutation.mutateAsync(createTagRequest);
      setNewTagName("");
      setShowNewTagInput(false);
    } catch (error) {
      console.error("Error creating tag:", error);
    }
  };

  return (
    <div className="max-w-4xl mx-auto">
      {/* Form */}
      <div className="bg-white shadow-sm border border-gray-200 rounded-lg">
        <div className="p-6 space-y-6">
          {/* Basic Information */}
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* Title */}
            <div className="md:col-span-2">
              <label
                htmlFor="title"
                className="block text-sm font-medium text-gray-700 mb-1"
              >
                Název záznamu *
              </label>
              <div className="relative">
                <FileText className="absolute left-3 top-3 h-4 w-4 text-gray-400" />
                <input
                  type="text"
                  id="title"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder="Zadejte název záznamu"
                  className={`block w-full pl-10 pr-3 py-2 border ${errors.title ? "border-red-300" : "border-gray-300"} rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500`}
                />
              </div>
              {errors.title && (
                <p className="mt-1 text-sm text-red-600">{errors.title}</p>
              )}
            </div>

            {/* Entry Date */}
            <div>
              <label
                htmlFor="entryDate"
                className="block text-sm font-medium text-gray-700 mb-1"
              >
                Datum záznamu *
              </label>
              <div className="relative">
                <Calendar className="absolute left-3 top-3 h-4 w-4 text-gray-400" />
                <input
                  type="date"
                  id="entryDate"
                  value={entryDate}
                  onChange={(e) => setEntryDate(e.target.value)}
                  className={`block w-full pl-10 pr-3 py-2 border ${errors.entryDate ? "border-red-300" : "border-gray-300"} rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500`}
                />
              </div>
              {errors.entryDate && (
                <p className="mt-1 text-sm text-red-600">{errors.entryDate}</p>
              )}
            </div>
          </div>

          {/* Content */}
          <div>
            <label
              htmlFor="content"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Obsah záznamu *
            </label>
            <textarea
              id="content"
              value={content}
              onChange={(e) => setContent(e.target.value)}
              rows={6}
              placeholder="Zadejte obsah záznamu..."
              className={`block w-full px-3 py-2 border ${errors.content ? "border-red-300" : "border-gray-300"} rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500`}
            />
            {errors.content && (
              <p className="mt-1 text-sm text-red-600">{errors.content}</p>
            )}
          </div>

          {/* Tags */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="block text-sm font-medium text-gray-700">
                Štítky
              </label>
              <button
                onClick={() => setShowNewTagInput(!showNewTagInput)}
                className="inline-flex items-center text-sm text-indigo-600 hover:text-indigo-700"
              >
                <Plus className="h-4 w-4 mr-1" />
                Nový štítek
              </button>
            </div>

            {showNewTagInput && (
              <div className="flex items-center gap-2 mb-3">
                <input
                  type="text"
                  value={newTagName}
                  onChange={(e) => setNewTagName(e.target.value)}
                  placeholder="Název nového štítku"
                  className="flex-1 px-3 py-2 border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
                />
                <button
                  onClick={handleCreateNewTag}
                  disabled={!newTagName.trim() || createTagMutation.isPending}
                  className="px-3 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 disabled:opacity-50"
                >
                  Vytvořit
                </button>
                <button
                  onClick={() => {
                    setShowNewTagInput(false);
                    setNewTagName("");
                  }}
                  className="px-3 py-2 bg-gray-300 text-gray-700 rounded-md hover:bg-gray-400"
                >
                  Zrušit
                </button>
              </div>
            )}

            <div className="flex flex-wrap gap-2">
              {tagsData?.tags?.map((tag) => (
                <button
                  key={tag.id}
                  onClick={() => handleTagToggle(tag.id!)}
                  className={`inline-flex items-center px-3 py-1 rounded-full text-xs font-medium ${
                    selectedTags.includes(tag.id!)
                      ? "bg-indigo-100 text-indigo-800 border border-indigo-200"
                      : "bg-gray-100 text-gray-700 border border-gray-200 hover:bg-gray-200"
                  }`}
                >
                  <Tag className="h-3 w-3 mr-1" />
                  {tag.name}
                </button>
              ))}
            </div>
          </div>

          {/* Associated Products */}
          <div>
            <div className="flex items-center justify-between mb-2">
              <label className="block text-sm font-medium text-gray-700">
                Přiřazené produkty
              </label>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => setUseTextInput(false)}
                  className={`inline-flex items-center px-3 py-1 text-xs font-medium rounded-md transition-colors ${
                    !useTextInput
                      ? "bg-indigo-100 text-indigo-800 border border-indigo-200"
                      : "bg-gray-100 text-gray-700 border border-gray-200 hover:bg-gray-200"
                  }`}
                >
                  <Search className="h-3 w-3 mr-1" />
                  Autocomplete
                </button>
                <button
                  onClick={() => setUseTextInput(true)}
                  className={`inline-flex items-center px-3 py-1 text-xs font-medium rounded-md transition-colors ${
                    useTextInput
                      ? "bg-indigo-100 text-indigo-800 border border-indigo-200"
                      : "bg-gray-100 text-gray-700 border border-gray-200 hover:bg-gray-200"
                  }`}
                >
                  <Type className="h-3 w-3 mr-1" />
                  Text
                </button>
              </div>
            </div>

            <CatalogAutocomplete<string>
              value={currentProduct}
              onSelect={handleProductSelect}
              placeholder={
                useTextInput
                  ? "Zadejte produktový prefix (např. COSM-001)..."
                  : "Začněte psát název nebo kód produktu..."
              }
              productTypes={PRODUCT_TYPE_FILTERS.ALL}
              itemAdapter={catalogItemToProductCode}
              displayValue={(value) => value}
              allowManualEntry={useTextInput}
              size="md"
            />

            {/* Mode-specific help text */}
            {useTextInput && (
              <div className="mt-1 text-xs text-gray-500">
                Tip: Zadejte produktový prefix a stiskněte Enter nebo klikněte
                mimo pole pro přidání
              </div>
            )}

            {/* Selected products */}
            <div className="flex flex-wrap gap-2 mt-3">
              {associatedProducts.map((productCode) => (
                <span
                  key={productCode}
                  className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-indigo-100 text-indigo-800"
                >
                  {productCode}
                  <button
                    onClick={() => handleRemoveProduct(productCode)}
                    className="ml-1 text-indigo-600 hover:text-indigo-800"
                  >
                    <X className="h-3 w-3" />
                  </button>
                </span>
              ))}
            </div>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="bg-gray-50 px-6 py-4 border-t border-gray-200 rounded-b-lg">
          <div className="flex items-center justify-between">
            {/* Delete button - only show in edit mode */}
            {isEdit && onDelete ? (
              <button
                onClick={onDelete}
                disabled={isLoading}
                className="inline-flex items-center px-4 py-2 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-md hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500 disabled:opacity-50"
              >
                <Trash2 className="h-4 w-4 mr-2" />
                Smazat záznam
              </button>
            ) : (
              <div></div>
            )}

            <div className="flex items-center gap-3">
              <button
                onClick={handleCancel}
                disabled={isLoading}
                className="inline-flex items-center px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
              >
                <X className="h-4 w-4 mr-2" />
                Zrušit
              </button>
              <button
                onClick={handleSave}
                disabled={isLoading}
                className="inline-flex items-center px-4 py-2 text-sm font-medium text-white bg-indigo-600 border border-transparent rounded-md hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
              >
                <Save className="h-4 w-4 mr-2" />
                {isLoading
                  ? "Ukládání..."
                  : isEdit
                    ? "Uložit změny"
                    : "Vytvořit záznam"}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
