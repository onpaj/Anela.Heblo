import { useState } from 'react';
import { Trash2 } from 'lucide-react';
import {
  TagWithCountDto,
  usePhotoTags,
  useCreateTag,
  useDeleteTag,
} from '../../../../api/hooks/usePhotobank';
import ConfirmDeleteTagDialog from './ConfirmDeleteTagDialog';

const TagsTab: React.FC = () => {
  const { data: tags = [], isLoading } = usePhotoTags();
  const createTag = useCreateTag();
  const deleteTag = useDeleteTag();

  const [tagName, setTagName] = useState('');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [alreadyExisted, setAlreadyExisted] = useState(false);
  const [tagToDelete, setTagToDelete] = useState<TagWithCountDto | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);

  const isFormValid = tagName.trim() !== '';

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isFormValid) return;
    setSubmitError(null);
    setAlreadyExisted(false);
    try {
      const result = await createTag.mutateAsync(tagName.trim());
      if (result?.alreadyExisted) {
        setAlreadyExisted(true);
      }
      setTagName('');
    } catch {
      setSubmitError('Nepodařilo se přidat štítek. Zkuste to znovu.');
    }
  };

  const handleDeleteClick = (tag: TagWithCountDto) => {
    if (tag.count === 0) {
      setDeletingId(tag.id);
      deleteTag.mutate(tag.id, { onSettled: () => setDeletingId(null) });
    } else {
      setTagToDelete(tag);
    }
  };

  const handleConfirmDelete = () => {
    if (tagToDelete) {
      setDeletingId(tagToDelete.id);
      deleteTag.mutate(tagToDelete.id, { onSettled: () => setDeletingId(null) });
      setTagToDelete(null);
    }
  };

  const handleCancelDelete = () => {
    setTagToDelete(null);
  };

  const sortedTags = [...tags].sort((a, b) => a.name.localeCompare(b.name));

  if (isLoading) {
    return <div className="text-sm text-gray-500">Načítání...</div>;
  }

  return (
    <div className="space-y-6">
      <form onSubmit={handleSubmit} className="space-y-3">
        <label htmlFor="tag-name" className="block text-sm font-semibold text-gray-700">
          Přidat štítek
        </label>
        <div className="flex gap-3">
          <input
            id="tag-name"
            type="text"
            value={tagName}
            onChange={(e) => setTagName(e.target.value)}
            placeholder="Název štítku"
            className="flex-1 px-2 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-blue focus:border-transparent"
          />
          <button
            type="submit"
            disabled={createTag.isPending || !isFormValid}
            className="px-3 py-1.5 text-sm bg-primary-blue text-white rounded-md hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Přidat štítek
          </button>
        </div>
        {submitError && (
          <p className="text-red-600 text-sm mt-2">{submitError}</p>
        )}
        {alreadyExisted && (
          <p className="text-sm text-gray-500 mt-2">Štítek již existuje</p>
        )}
      </form>

      <div className="border-t border-gray-200 pt-4">
        {sortedTags.length === 0 ? (
          <p className="text-sm text-gray-500">Žádné štítky nejsou vytvořeny.</p>
        ) : (
          <ul className="divide-y divide-gray-100">
            {sortedTags.map((tag) => (
              <li key={tag.id} className="flex items-center justify-between py-2">
                <span className="text-sm text-gray-800">{tag.name}</span>
                <div className="flex items-center gap-3">
                  <span className="px-1.5 py-0.5 rounded text-xs bg-gray-100 text-gray-600">
                    {tag.count} fotek
                  </span>
                  <button
                    onClick={() => handleDeleteClick(tag)}
                    disabled={deletingId === tag.id}
                    className="p-1 text-gray-400 hover:text-red-500 rounded disabled:opacity-50"
                    aria-label={`Smazat štítek ${tag.name}`}
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      <ConfirmDeleteTagDialog
        isOpen={tagToDelete !== null}
        tagName={tagToDelete?.name ?? ''}
        assignmentCount={tagToDelete?.count ?? 0}
        onConfirm={handleConfirmDelete}
        onCancel={handleCancelDelete}
      />
    </div>
  );
};

export default TagsTab;
