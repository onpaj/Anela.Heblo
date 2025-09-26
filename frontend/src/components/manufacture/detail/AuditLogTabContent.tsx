import React from "react";
import { ScrollText } from "lucide-react";

interface AuditLogTabContentProps {
  order: any;
  formatDateTime: (date: Date | string | undefined) => string;
  getAuditActionLabel: (action: any) => string;
}

export const AuditLogTabContent: React.FC<AuditLogTabContentProps> = ({
  order,
  formatDateTime,
  getAuditActionLabel,
}) => {
  return (
    <div>
      <h3 className="text-lg font-medium text-gray-900 mb-4 flex items-center">
        <ScrollText className="h-5 w-5 mr-2 text-indigo-600" />
        Audit log
      </h3>
      {order.auditLog && order.auditLog.length > 0 ? (
        <div className="bg-white shadow rounded-lg overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Čas
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Akce
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Uživatel
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Detaily
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {order.auditLog.map((logEntry: any, index: number) => (
                <tr key={index}>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    {formatDateTime(logEntry.timestamp)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {getAuditActionLabel(logEntry.action)}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {logEntry.user || "-"}
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-500">
                    {logEntry.details || "-"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-gray-500 text-center py-8">Žádné záznamy v audit logu.</p>
      )}
    </div>
  );
};

export default AuditLogTabContent;