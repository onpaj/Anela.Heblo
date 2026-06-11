import React, { useMemo } from "react";
import { useGroups } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface IncludedGroupsPickerProps {
  currentGroupId: string;
  value: string[];
  onChange: (groupIds: string[]) => void;
  fillHeight?: boolean;
}

export default function IncludedGroupsPicker({ currentGroupId, value, onChange, fillHeight }: IncludedGroupsPickerProps) {
  const groups = useGroups();

  const items: TransferItem[] = useMemo(
    () =>
      (groups.data?.groups ?? [])
        .filter((g) => g.id !== currentGroupId)
        .map((g) => ({ id: g.id ?? "", label: g.name ?? g.id ?? "" })),
    [groups.data, currentGroupId]
  );

  if (groups.isLoading) return <div className="text-gray-500 text-sm">Loading groups…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      labels={{ available: "Available groups", assigned: "Included groups" }}
      fillHeight={fillHeight}
      searchable
      searchPlaceholder="Search groups…"
    />
  );
}
