import React, { useState, useEffect } from 'react';
import Select from 'react-select';
import {
  MeetingTranscriptDto,
  MeetingUserDto,
  useUpdateMeetingAccess,
} from '../../../../api/hooks/useMeetingTasks';

interface ManageAccessModalProps {
  isOpen: boolean;
  onClose: () => void;
  transcript: MeetingTranscriptDto;
  users: MeetingUserDto[];
}

type AccessLevel = 'Private' | 'Public' | 'Restricted';

interface UserOption {
  value: string;
  label: string;
}

export const ManageAccessModal: React.FC<ManageAccessModalProps> = ({
  isOpen,
  onClose,
  transcript,
  users,
}) => {
  const [accessLevel, setAccessLevel] = useState<AccessLevel>(
    (transcript.accessLevel as AccessLevel) ?? 'Private'
  );
  const [selectedEmails, setSelectedEmails] = useState<string[]>(
    transcript.accessGrants?.map((g) => g.userEmail) ?? []
  );

  const { mutate: updateAccess, isPending, error } = useUpdateMeetingAccess();

  useEffect(() => {
    if (isOpen) {
      setAccessLevel((transcript.accessLevel as AccessLevel) ?? 'Private');
      setSelectedEmails(transcript.accessGrants?.map((g) => g.userEmail) ?? []);
    }
  }, [isOpen, transcript]);

  const userOptions: UserOption[] = users.map((u) => ({
    value: u.email,
    label: `${u.displayName} (${u.email})`,
  }));

  const selectedOptions = userOptions.filter((o) => selectedEmails.includes(o.value));

  const handleSave = () => {
    updateAccess(
      {
        transcriptId: transcript.id,
        accessLevel,
        restrictedUserEmails: accessLevel === 'Restricted' ? selectedEmails : [],
      },
      { onSuccess: onClose }
    );
  };

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
      onClick={onClose}
    >
      <div
        className="bg-white rounded-xl shadow-lg p-6 w-full max-w-md"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-semibold mb-4">Spravovat přístup ke schůzce</h2>

        <fieldset className="mb-4 space-y-2">
          {(['Private', 'Public', 'Restricted'] as AccessLevel[]).map((level) => (
            <label key={level} className="flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                name="accessLevel"
                value={level}
                checked={accessLevel === level}
                onChange={() => setAccessLevel(level)}
              />
              <span className="text-sm">
                {level === 'Private' && 'Soukromé — vidí pouze správci schůzek'}
                {level === 'Public' && 'Veřejné — vidí všichni přihlášení uživatelé'}
                {level === 'Restricted' && 'Omezené — vidí pouze vybraní uživatelé'}
              </span>
            </label>
          ))}
        </fieldset>

        {accessLevel === 'Restricted' && (
          <div className="mb-4">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Oprávnění uživatelé
            </label>
            <Select
              isMulti
              options={userOptions}
              value={selectedOptions}
              onChange={(opts) => setSelectedEmails(opts.map((o) => o.value))}
              placeholder="Vyberte uživatele..."
              menuPortalTarget={document.body}
              styles={{ menuPortal: (base) => ({ ...base, zIndex: 9999 }) }}
              classNamePrefix="react-select"
            />
          </div>
        )}

        {error && (
          <p className="text-sm text-red-600 mb-3">Nepodařilo se uložit přístup. Zkuste to znovu.</p>
        )}

        <div className="flex justify-end gap-2 mt-4">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
          >
            Zrušit
          </button>
          <button
            onClick={handleSave}
            disabled={isPending || (accessLevel === 'Restricted' && selectedEmails.length === 0)}
            className="px-4 py-2 text-sm rounded-lg bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {isPending ? 'Ukládám...' : 'Uložit'}
          </button>
        </div>
      </div>
    </div>
  );
};
