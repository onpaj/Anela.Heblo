import { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { usePermissionsContext } from "../../auth/PermissionsContext";

interface RequireAccessProps {
  requiredRole?: string;
  children: ReactNode;
}

export function RequireAccess({ requiredRole, children }: RequireAccessProps) {
  const { hasPermission, isLoading } = usePermissionsContext();

  if (isLoading) {
    return null; // wait for /api/auth/me before deciding
  }

  if (requiredRole && !hasPermission(requiredRole)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
