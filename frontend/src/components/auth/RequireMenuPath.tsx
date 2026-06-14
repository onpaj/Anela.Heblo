import React from 'react';
import { Navigate } from 'react-router-dom';
import { usePermissionsContext } from '../../auth/PermissionsContext';
import { ACCESS_ROUTES } from '../../auth/accessMatrix.generated';

interface Props {
  path: string;
  redirectTo?: string;
  children: React.ReactNode;
}

const RequireMenuPath: React.FC<Props> = ({ path, redirectTo = '/', children }) => {
  const { hasPermission, isLoading } = usePermissionsContext();
  if (isLoading) return null;
  const req = ACCESS_ROUTES[path];
  if (!req) return <Navigate to={redirectTo} replace />;
  if (!req.permissions.every(p => hasPermission(p)))
    return <Navigate to={redirectTo} replace />;
  return <>{children}</>;
};

export default RequireMenuPath;
