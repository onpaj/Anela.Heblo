import React, { createContext, useContext, ReactNode } from "react";
import { usePermissions } from "../api/hooks/usePermissions";

interface PermissionsContextValue {
  permissions: string[];
  isSuperUser: boolean;
  groups: string[];
  isLoading: boolean;
  hasPermission: (perm: string) => boolean;
}

const PermissionsContext = createContext<PermissionsContextValue | undefined>(undefined);

interface ProviderProps {
  isAuthenticated: boolean;
  children: ReactNode;
}

export const PermissionsProvider: React.FC<ProviderProps> = ({ isAuthenticated, children }) => {
  const { data, isLoading } = usePermissions(isAuthenticated);

  const value: PermissionsContextValue = {
    permissions: data?.permissions ?? [],
    isSuperUser: data?.isSuperUser ?? false,
    groups: data?.groups ?? [],
    isLoading: isAuthenticated && isLoading,
    hasPermission: (perm: string) =>
      (data?.isSuperUser ?? false) || (data?.permissions ?? []).includes(perm),
  };

  return <PermissionsContext.Provider value={value}>{children}</PermissionsContext.Provider>;
};

export const usePermissionsContext = (): PermissionsContextValue => {
  const ctx = useContext(PermissionsContext);
  if (!ctx) throw new Error("usePermissionsContext must be used within PermissionsProvider");
  return ctx;
};
