import React, { useState } from 'react';
import {
  useUpsertEmployeeMutation,
  type OvertimeEmployeeDto,
  type AvailableLogetoPersonDto,
} from '../../api/hooks/useOvertime';
import { formatLocalDate, getLocalToday } from '../../utils/dateUtils';
import { formatNumber } from '../../utils/formatters';

interface EmployeeRowProps {
  employee: OvertimeEmployeeDto;
  canWrite: boolean;
  isSaving: boolean;
  onSave: (baselineHours: number, baselineDate: string, isActive: boolean) => void;
}

const EmployeeRow: React.FC<EmployeeRowProps> = ({ employee, canWrite, isSaving, onSave }) => {
  const [baselineHours, setBaselineHours] = useState(String(employee.baselineHours ?? 0));
  const [baselineDate, setBaselineDate] = useState(
    employee.baselineDate ? formatLocalDate(employee.baselineDate) : formatLocalDate(getLocalToday()),
  );

  const handleSave = () => {
    const hoursValue = parseFloat(baselineHours);
    if (Number.isNaN(hoursValue)) return;
    onSave(hoursValue, baselineDate, employee.isActive ?? true);
  };

  const handleToggleActive = () => {
    const hoursValue = parseFloat(baselineHours);
    onSave(
      Number.isNaN(hoursValue) ? (employee.baselineHours ?? 0) : hoursValue,
      baselineDate,
      !(employee.isActive ?? true),
    );
  };

  return (
    <tr className="border-b border-gray-200 dark:border-graphite-border last:border-0">
      <td className="py-2 pr-4 text-sm text-gray-900 dark:text-graphite-text">{employee.displayName}</td>
      <td className="py-2 pr-4">
        <input
          type="number"
          step="0.01"
          value={baselineHours}
          onChange={(e) => setBaselineHours(e.target.value)}
          disabled={!canWrite}
          aria-label={`Úvazek ${employee.displayName}`}
          className="border border-gray-300 dark:border-graphite-border rounded px-2 py-1 text-sm w-24 bg-white dark:bg-graphite-surface disabled:opacity-50"
        />
      </td>
      <td className="py-2 pr-4">
        <input
          type="date"
          value={baselineDate}
          onChange={(e) => setBaselineDate(e.target.value)}
          disabled={!canWrite}
          aria-label={`Datum od ${employee.displayName}`}
          className="border border-gray-300 dark:border-graphite-border rounded px-2 py-1 text-sm bg-white dark:bg-graphite-surface disabled:opacity-50"
        />
      </td>
      <td className="py-2 pr-4 text-sm text-gray-700 dark:text-graphite-muted">
        {formatNumber(employee.currentBalance ?? null)}
      </td>
      <td className="py-2 pr-4 text-center">
        <input
          type="checkbox"
          checked={employee.isActive ?? true}
          onChange={handleToggleActive}
          disabled={!canWrite || isSaving}
          aria-label={`Aktivní ${employee.displayName}`}
        />
      </td>
      <td className="py-2 text-right">
        {canWrite && (
          <button
            onClick={handleSave}
            disabled={isSaving}
            className="px-3 py-1 text-xs font-medium text-indigo-700 dark:text-graphite-accent hover:underline disabled:opacity-50"
          >
            Uložit
          </button>
        )}
      </td>
    </tr>
  );
};

interface EmployeeSettingsSectionProps {
  employees: OvertimeEmployeeDto[];
  availablePeople: AvailableLogetoPersonDto[];
  canWrite: boolean;
}

const EmployeeSettingsSection: React.FC<EmployeeSettingsSectionProps> = ({
  employees,
  availablePeople,
  canWrite,
}) => {
  const upsertEmployee = useUpsertEmployeeMutation();
  const [selectedPersonId, setSelectedPersonId] = useState('');

  const handleAddEmployee = () => {
    const person = availablePeople.find((p) => p.personId === selectedPersonId);
    if (!person?.personId) return;
    upsertEmployee.mutate({
      personId: person.personId,
      displayName: person.fullName ?? '',
      baselineHours: 0,
      baselineDate: formatLocalDate(getLocalToday()),
      isActive: true,
    });
    setSelectedPersonId('');
  };

  return (
    <div>
      <table className="min-w-full">
        <thead>
          <tr className="text-left text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider">
            <th className="py-2 pr-4">Zaměstnanec</th>
            <th className="py-2 pr-4">Úvazek (h/den)</th>
            <th className="py-2 pr-4">Datum od</th>
            <th className="py-2 pr-4">Zůstatek</th>
            <th className="py-2 pr-4 text-center">Aktivní</th>
            <th className="py-2" />
          </tr>
        </thead>
        <tbody>
          {employees.map((employee) => (
            <EmployeeRow
              key={employee.personId}
              employee={employee}
              canWrite={canWrite}
              isSaving={upsertEmployee.isPending}
              onSave={(baselineHours, baselineDate, isActive) =>
                upsertEmployee.mutate({
                  personId: employee.personId as string,
                  displayName: employee.displayName ?? '',
                  baselineHours,
                  baselineDate,
                  isActive,
                })
              }
            />
          ))}
        </tbody>
      </table>

      {employees.length === 0 && (
        <p className="text-sm text-gray-500 dark:text-graphite-muted py-2">Žádní sledovaní zaměstnanci</p>
      )}

      {canWrite && availablePeople.length > 0 && (
        <div className="flex items-center gap-2 mt-3">
          <select
            value={selectedPersonId}
            onChange={(e) => setSelectedPersonId(e.target.value)}
            aria-label="Vyberte osobu k přidání"
            className="border border-gray-300 dark:border-graphite-border rounded px-2 py-1 text-sm bg-white dark:bg-graphite-surface"
          >
            <option value="">Vyberte osobu…</option>
            {availablePeople.map((p) => (
              <option key={p.personId} value={p.personId}>
                {p.fullName}
              </option>
            ))}
          </select>
          <button
            onClick={handleAddEmployee}
            disabled={!selectedPersonId || upsertEmployee.isPending}
            className="inline-flex items-center px-3 py-1.5 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium rounded-md transition-colors disabled:opacity-50"
          >
            Přidat
          </button>
        </div>
      )}
    </div>
  );
};

export default EmployeeSettingsSection;
