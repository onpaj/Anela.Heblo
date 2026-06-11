import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";

/**
 * Compares two draft objects for equality, treating array fields as
 * order-insensitive (e.g. permission/group id lists). Avoids false "dirty"
 * positives when the user reorders items without changing the set.
 */
export function draftsEqual<T extends object>(
  a: T | null,
  b: T | null,
): boolean {
  if (a === b) return true;
  if (!a || !b) return false;

  const aRec = a as Record<string, unknown>;
  const bRec = b as Record<string, unknown>;
  const keys = Object.keys(aRec);
  if (keys.length !== Object.keys(bRec).length) return false;

  return keys.every((key) => {
    const av = aRec[key];
    const bv = bRec[key];
    if (Array.isArray(av) && Array.isArray(bv)) {
      if (av.length !== bv.length) return false;
      const sortedA = [...av].sort();
      const sortedB = [...bv].sort();
      return sortedA.every((v, i) => v === sortedB[i]);
    }
    return av === bv;
  });
}

interface UnsavedChangesDialogState {
  dialogProps: {
    isOpen: boolean;
    isSaving: boolean;
    onSave: () => void;
    onDiscard: () => void;
    onKeepEditing: () => void;
  };
  requestNavigation: (to: string) => void;
}

/**
 * Intercepts explicit "leave the editor" navigations (Cancel / ← back) when the
 * form has unsaved changes, surfacing a Save / Discard / Keep editing dialog.
 * Also registers a `beforeunload` prompt to cover hard refresh / tab close.
 *
 * Note: this does NOT guard sidebar-menu clicks or in-app browser Back — the app
 * uses BrowserRouter, where react-router's useBlocker is a no-op. Route those
 * "leave" buttons through `requestNavigation` instead of calling navigate directly.
 */
export function useUnsavedChangesDialog(
  isDirty: boolean,
  save: () => Promise<boolean>,
): UnsavedChangesDialogState {
  const navigate = useNavigate();
  const [pendingTo, setPendingTo] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const requestNavigation = (to: string) => {
    if (isDirty) {
      setPendingTo(to);
    } else {
      navigate(to);
    }
  };

  const onKeepEditing = () => setPendingTo(null);

  const onDiscard = () => {
    const to = pendingTo;
    setPendingTo(null);
    if (to) navigate(to);
  };

  const onSave = async () => {
    setIsSaving(true);
    const ok = await save();
    setIsSaving(false);
    if (!ok) return; // stay open so the user sees the validation/save error
    const to = pendingTo;
    setPendingTo(null);
    if (to) navigate(to);
  };

  useEffect(() => {
    if (!isDirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [isDirty]);

  return {
    dialogProps: { isOpen: pendingTo !== null, isSaving, onSave, onDiscard, onKeepEditing },
    requestNavigation,
  };
}
