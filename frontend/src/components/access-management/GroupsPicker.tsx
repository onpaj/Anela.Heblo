import React, { useMemo } from "react";
import { useGroups } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface GroupsPickerProps {
  value: string[];
  onChange: (groupIds: string[]) => void;
}

export default function GroupsPicker({ value, onChange }: GroupsPickerProps) {
  const groups = useGroups();

  const items: TransferItem[] = useMemo(
    () =>
      (groups.data?.groups ?? []).map((g) => ({
        id: g.id ?? "",
        label: g.name ?? g.id ?? "",
      })),
    [groups.data]
  );

  if (groups.isLoading) return <div className="text-gray-500 text-sm">Loading groups…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      labels={{ available: "Available groups", assigned: "Member of" }}
    />
  );
}
