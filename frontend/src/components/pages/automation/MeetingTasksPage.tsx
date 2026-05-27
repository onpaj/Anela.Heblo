import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { Clock, CheckCircle, CheckCircle2, ChevronLeft, ChevronRight, RefreshCw } from "lucide-react";
import {
  MeetingTranscriptDto,
  TranscriptStatus,
  useMeetingTasksList,
} from "../../../api/hooks/useMeetingTasks";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";
import { useScreenView } from "../../../telemetry/useScreenView";

const PAGE_SIZE = 20;

type StatusBadgeProps = { status: string };

type AccessLevelBadgeProps = { accessLevel: string };

function AccessLevelBadge({ accessLevel }: AccessLevelBadgeProps) {
  const colorMap: Record<string, string> = {
    Private: "bg-gray-100 text-gray-700",
    Public: "bg-green-100 text-green-800",
    Restricted: "bg-orange-100 text-orange-800",
  };
  const labelMap: Record<string, string> = {
    Private: "Soukrome",
    Public: "Verejne",
    Restricted: "Omezene",
  };
  const color = colorMap[accessLevel] ?? "bg-gray-100 text-gray-700";
  const label = labelMap[accessLevel] ?? accessLevel;
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${color}`}>
      {label}
    </span>
  );
}

function StatusBadge({ status }: StatusBadgeProps) {
  const colorMap: Record<string, string> = {
    PendingReview: "bg-yellow-100 text-yellow-800",
    Approved: "bg-green-100 text-green-800",
    PartiallyApproved: "bg-blue-100 text-blue-800",
  };
  const labelMap: Record<string, string> = {
    PendingReview: "Ke kontrole",
    Approved: "Schvaleno",
    PartiallyApproved: "Castecne",
  };
  const iconMap: Record<string, React.ReactNode> = {
    PendingReview: <Clock className="w-3.5 h-3.5 mr-1" />,
    Approved: <CheckCircle className="w-3.5 h-3.5 mr-1" />,
    PartiallyApproved: <CheckCircle2 className="w-3.5 h-3.5 mr-1" />,
  };
  const color = colorMap[status] ?? "bg-gray-100 text-gray-800";
  const label = labelMap[status] ?? status;
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${color}`}>
      {iconMap[status]}
      {label}
    </span>
  );
}

const MeetingTasksPage: React.FC = () => {
  useScreenView('Automation', 'MeetingTasks');
  const navigate = useNavigate();
  const [statusFilter, setStatusFilter] = useState<string | undefined>(undefined);
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [searchInTranscript, setSearchInTranscript] = useState(false);
  const [debouncedSearch, setDebouncedSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(searchInput), 300);
    return () => clearTimeout(timer);
  }, [searchInput]);

  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, searchInTranscript]);

  const { data, isLoading, refetch, isFetching } = useMeetingTasksList(
    statusFilter,
    debouncedSearch || undefined,
    searchInTranscript,
    page,
    PAGE_SIZE,
  );

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  const handleFilter = (next: string | undefined) => {
    setStatusFilter(next);
    setPage(1);
  };

  const filterButton = (label: string, value: string | undefined) => {
    const active = statusFilter === value;
    return (
      <button
        key={label}
        type="button"
        onClick={() => handleFilter(value)}
        className={`px-3 py-1.5 rounded-md text-sm font-medium border transition-colors ${
          active
            ? "bg-indigo-600 text-white border-indigo-600"
            : "bg-white text-gray-700 border-gray-300 hover:bg-gray-50"
        }`}
      >
        {label}
      </button>
    );
  };

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between">
          <h1 className="text-3xl font-bold text-gray-900">Porady</h1>
          <button
            type="button"
            title="Obnovit"
            disabled={isFetching}
            onClick={() => refetch()}
            className="inline-flex items-center px-2 py-1 border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <RefreshCw className={`w-4 h-4${isFetching ? " animate-spin" : ""}`} />
          </button>
        </div>
        <p className="mt-2 text-gray-600">Validace AI-extrahovanych ukolu ze schuzek pred odeslanim do Microsoft TODO</p>
      </div>

      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8 flex flex-wrap items-center gap-2">
        {filterButton("Vse", undefined)}
        {filterButton("Ke kontrole", "PendingReview" as TranscriptStatus)}
        {filterButton("Schvaleno", "Approved" as TranscriptStatus)}
        <input
          type="text"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          placeholder="Hledat..."
          className="px-3 py-1.5 rounded-md text-sm border border-gray-300 focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-indigo-500"
        />
        <label className="flex items-center gap-1.5 text-sm text-gray-700 cursor-pointer select-none">
          <input
            type="checkbox"
            checked={searchInTranscript}
            onChange={(e) => setSearchInTranscript(e.target.checked)}
            className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
          />
          Hledat i v prepisu
        </label>
      </div>

      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        <div className="bg-white shadow-sm rounded-lg border border-gray-200 overflow-hidden">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Predmet</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Prijato</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Ulohy</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Pristup</th>
                <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Stav</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {isLoading && (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-center text-sm text-gray-500">Nacitani...</td>
                </tr>
              )}
              {!isLoading && items.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-4 py-6 text-center text-sm text-gray-500">Zadne zaznamy</td>
                </tr>
              )}
              {!isLoading && items.map((row: MeetingTranscriptDto) => (
                <tr
                  key={row.id}
                  onClick={() => navigate(`/automation/meeting-tasks/${row.id}`)}
                  className={`cursor-pointer hover:bg-gray-50 ${row.status === "PendingReview" ? "bg-yellow-50" : ""}`}
                >
                  <td className="px-4 py-2 text-sm text-gray-900">{row.subject}</td>
                  <td className="px-4 py-2 text-sm text-gray-700">
                    {new Date(row.receivedAt).toLocaleDateString("cs-CZ")}
                  </td>
                  <td className="px-4 py-2 text-sm text-gray-700">
                    {row.taskCount}
                    {row.approvedTaskCount > 0 && (
                      <span className="ml-1 text-xs text-gray-500">
                        ({row.approvedTaskCount} schvaleno)
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-2"><AccessLevelBadge accessLevel={row.accessLevel} /></td>
                  <td className="px-4 py-2"><StatusBadge status={row.status} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="flex items-center justify-between mt-3 text-sm text-gray-700">
            <div>Strana {page} z {totalPages} ({totalCount} celkem)</div>
            <div className="flex gap-2">
              <button
                type="button"
                title="Predchozi strana"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="inline-flex items-center px-2 py-1 border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
              <button
                type="button"
                title="Dalsi strana"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="inline-flex items-center px-2 py-1 border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default MeetingTasksPage;
