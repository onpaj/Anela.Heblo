import React, { useMemo } from "react";
import { useCatalogue } from "../../api/hooks/useAccessManagement";
import TransferList, { TransferItem } from "./TransferList";

interface PermissionPickerProps {
  value: string[];
  onChange: (permissions: string[]) => void;
}

export default function PermissionPicker({ value, onChange }: PermissionPickerProps) {
  const catalogue = useCatalogue();

  const { items, sectionByPermission } = useMemo(() => {
    const allItems: TransferItem[] = [];
    const sectionMap: Record<string, string> = {};
    for (const feature of catalogue.data?.features ?? []) {
      const addLevel = (level: string) => {
        const id = `${feature.key}.${level}`;
        allItems.push({ id, label: `${feature.label ?? feature.key} — ${level}` });
        sectionMap[id] = feature.section ?? "";
      };
      addLevel("read");
      if (feature.hasWrite) addLevel("write");
      if (feature.hasAdmin) addLevel("admin");
    }
    return { items: allItems, sectionByPermission: sectionMap };
  }, [catalogue.data]);

  if (catalogue.isLoading) return <div className="text-gray-500 text-sm">Loading permissions…</div>;

  return (
    <TransferList
      available={items}
      assignedIds={value}
      onChange={onChange}
      groupBy={(item) => sectionByPermission[item.id] ?? ""}
      labels={{ available: "Available permissions", assigned: "Assigned permissions" }}
    />
  );
}
