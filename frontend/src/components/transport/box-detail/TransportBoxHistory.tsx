import React from "react";
import { Calendar, Clock, User } from "lucide-react";
import {
  TransportBoxHistoryProps,
  stateLabels,
  stateColors,
} from "./TransportBoxTypes";

const TransportBoxHistory: React.FC<TransportBoxHistoryProps> = ({
  transportBox,
  formatDate,
}) => {
  return (
    <div>
      {transportBox.stateLog && transportBox.stateLog.length > 0 ? (
        <div
          className="overflow-auto"
          style={{ minHeight: "300px", maxHeight: "50vh" }}
        >
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50 sticky top-0 z-10">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Datum
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Stav
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Uživatel
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                  Popis
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {transportBox.stateLog.map((log) => (
                <tr key={log.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    <div className="flex items-center gap-1">
                      <Calendar className="h-4 w-4 text-gray-400" />
                      {formatDate(log.stateDate)}
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <span
                      className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                        stateColors[log.state || ""] ||
                        "bg-gray-100 text-gray-800"
                      }`}
                    >
                      {stateLabels[log.state || ""] || log.state}
                    </span>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900">
                    <div className="flex items-center gap-1">
                      <User className="h-4 w-4 text-gray-400" />
                      {log.user || "-"}
                    </div>
                  </td>
                  <td className="px-6 py-4 text-sm text-gray-900">
                    {log.description || "-"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="text-center py-8">
          <Clock className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">
            Žádná historie
          </h3>
          <p className="mt-1 text-sm text-gray-500">
            Pro tento transportní box není k dispozici historie stavů.
          </p>
        </div>
      )}
    </div>
  );
};

export default TransportBoxHistory;
