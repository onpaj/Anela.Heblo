import { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "../../auth/useAuth";

interface RequireAccessProps {
  requiredRole?: string;
  children: ReactNode;
}

export function RequireAccess({ requiredRole, children }: RequireAccessProps) {
  const { getUserInfo } = useAuth();
  const userInfo = getUserInfo();
  const roles = userInfo?.roles ?? [];

  if (requiredRole && !roles.includes(requiredRole)) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}
