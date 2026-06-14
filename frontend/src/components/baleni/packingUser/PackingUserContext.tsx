import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";

export interface SelectedPackingUser {
  id: string;
  displayName: string;
}

const STORAGE_KEY = "heblo.baleni.packingUser";

interface PackingUserContextValue {
  current: SelectedPackingUser | null;
  setCurrent: (user: SelectedPackingUser) => void;
  clear: () => void;
  isPickerOpen: boolean;
  openPicker: () => void;
  closePicker: () => void;
}

const PackingUserContext = createContext<PackingUserContextValue | null>(null);

function readStored(): SelectedPackingUser | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as SelectedPackingUser) : null;
  } catch {
    return null;
  }
}

export function PackingUserProvider({ children }: { children: ReactNode }) {
  const [current, setCurrentState] = useState<SelectedPackingUser | null>(readStored);
  const [isPickerOpen, setPickerOpen] = useState(false);

  const setCurrent = useCallback((user: SelectedPackingUser) => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    setCurrentState(user);
    setPickerOpen(false);
  }, []);

  const clear = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY);
    setCurrentState(null);
  }, []);

  const value = useMemo<PackingUserContextValue>(
    () => ({
      current,
      setCurrent,
      clear,
      isPickerOpen,
      openPicker: () => setPickerOpen(true),
      closePicker: () => setPickerOpen(false),
    }),
    [current, setCurrent, clear, isPickerOpen],
  );

  return <PackingUserContext.Provider value={value}>{children}</PackingUserContext.Provider>;
}

export function usePackingUser(): PackingUserContextValue {
  const ctx = useContext(PackingUserContext);
  if (!ctx) {
    throw new Error("usePackingUser must be used within a PackingUserProvider");
  }
  return ctx;
}
