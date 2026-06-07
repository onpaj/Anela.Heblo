import React, { useMemo, useState } from "react";
import {
  useEntraAccessUsers,
  useUsers,
  useAddGroupMember,
} from "../../api/hooks/useAccessManagement";
import { useToast } from "../../contexts/ToastContext";
import { AddGroupMemberRequest } from "../../api/generated/api-client";

interface EntraMemberSearchProps {
  groupId: string;
  currentMemberIds: string[];
  onMemberAdded: (userId: string) => void;
}

export default function EntraMemberSearch({
  groupId,
  currentMemberIds,
  onMemberAdded,
}: EntraMemberSearchProps) {
  const entraUsers = useEntraAccessUsers();
  const provisionedUsers = useUsers();
  const addMember = useAddGroupMember();
  const toast = useToast();
  const [query, setQuery] = useState("");
  const [isOpen, setIsOpen] = useState(false);

  const currentMemberEntraIds = useMemo(() => {
    const inGroup = (provisionedUsers.data?.users ?? []).filter((u) =>
      currentMemberIds.includes(u.id ?? "")
    );
    return new Set(
      inGroup
        .map((u) => u.entraObjectId)
        .filter((id): id is string => Boolean(id))
    );
  }, [provisionedUsers.data, currentMemberIds]);

  const candidates = useMemo(() => {
    const available = (entraUsers.data?.users ?? []).filter(
      (u) => !currentMemberEntraIds.has(u.entraObjectId ?? "")
    );
    if (!query.trim()) return available;
    const q = query.toLowerCase();
    return available.filter(
      (u) =>
        u.displayName?.toLowerCase().includes(q) ||
        u.email?.toLowerCase().includes(q)
    );
  }, [entraUsers.data, currentMemberEntraIds, query]);

  const handleSelect = async (user: {
    entraObjectId?: string | null;
    email?: string | null;
    displayName?: string | null;
  }) => {
    if (!user.entraObjectId) return;
    setQuery("");
    setIsOpen(false);
    try {
      const result = await addMember.mutateAsync({
        groupId,
        request: new AddGroupMemberRequest({
          entraObjectId: user.entraObjectId,
          email: user.email ?? "",
          displayName: user.displayName ?? "",
        }),
      });
      if (result.user?.id) {
        onMemberAdded(result.user.id);
        toast.showSuccess(
          "Member added",
          `${user.displayName ?? user.email} added to group`
        );
      }
    } catch {
      toast.showError("Add failed", "Could not add member to group");
    }
  };

  const isLoading =
    entraUsers.isLoading || provisionedUsers.isLoading || addMember.isPending;

  return (
    <div className="relative mb-4">
      <label className="block text-xs font-semibold text-gray-600 uppercase tracking-wider mb-1">
        Add Entra user
      </label>
      <input
        type="text"
        value={query}
        onChange={(e) => {
          setQuery(e.target.value);
          setIsOpen(true);
        }}
        onFocus={() => setIsOpen(true)}
        onBlur={() => setTimeout(() => setIsOpen(false), 150)}
        placeholder={
          entraUsers.isLoading
            ? "Loading Entra users…"
            : addMember.isPending
            ? "Adding…"
            : "Search by name or email…"
        }
        disabled={isLoading}
        className="w-full rounded border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400 disabled:bg-gray-50 disabled:text-gray-400"
      />
      {isOpen && candidates.length > 0 && (
        <ul className="absolute z-20 mt-1 w-full bg-white border border-gray-200 rounded shadow-lg max-h-48 overflow-y-auto">
          {candidates.map((u) => (
            <li
              key={u.entraObjectId}
              onMouseDown={() => handleSelect(u)}
              className="flex flex-col px-3 py-2 hover:bg-indigo-50 cursor-pointer"
            >
              <span className="text-sm text-gray-900">{u.displayName}</span>
              <span className="text-xs text-gray-500">{u.email}</span>
            </li>
          ))}
        </ul>
      )}
      {isOpen && !entraUsers.isLoading && candidates.length === 0 && query.trim() && (
        <div className="absolute z-20 mt-1 w-full bg-white border border-gray-200 rounded shadow-lg px-3 py-2 text-sm text-gray-400">
          No matching Entra users
        </div>
      )}
    </div>
  );
}
