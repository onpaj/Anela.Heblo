import React, { useMemo } from "react";
import { useUsers } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface MembersPickerProps {
  value: string[];
  onChange: (userIds: string[]) => void;
  fillHeight?: boolean;
}

export default function MembersPicker({ value, onChange, fillHeight }: MembersPickerProps) {
  const users = useUsers();

  const items: TransferItem[] = useMemo(
    () =>
      (users.data?.users ?? []).map((u) => ({
        id: u.id ?? "",
        label: u.displayName ?? u.email ?? u.id ?? "",
        sublabel: u.email,
        badge: u.lastLoginAt == null ? "Never logged in" : undefined,
      })),
    [users.data]
  );

  if (users.isLoading) return <div className="text-gray-500 dark:text-graphite-muted text-sm">Loading users…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      labels={{ available: "All users", assigned: "Members" }}
      fillHeight={fillHeight}
    />
  );
}
