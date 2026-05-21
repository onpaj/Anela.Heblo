import React, { useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { Link, useParams } from "react-router-dom";
import {
  ArrowLeft, Check, X, Plus, Send, CheckCheck, Clock, CheckCircle, CheckCircle2,
  ChevronDown, ChevronRight, AlertTriangle, RefreshCw, Download,
} from "lucide-react";
import {
  MeetingUserDto,
  ProposedTaskDto,
  ProposedTaskStatus,
  TaskFormData,
  TranscriptStatus,
  useAddProposedTask,
  useExplainMeetingSummary,
  useMeetingTaskDetail,
  useMeetingUsers,
  useReimportMeeting,
  useSubmitToTodo,
  useUpdateProposedTask,
  useUpdateProposedTaskStatus,
} from "../../../api/hooks/useMeetingTasks";
import { useMeetingManagerPermission } from '../../../api/hooks/useMeetingManagerPermission';
import { useExplainSelection } from './explain/useExplainSelection';
import { ExplainTooltip } from './explain/ExplainTooltip';
import { ExplainModal } from './explain/ExplainModal';
import { ManageAccessModal } from './access/ManageAccessModal';
import { downloadTextFile, sanitizeFilename } from "../../../utils/downloadTextFile";
import { PAGE_CONTAINER_HEIGHT } from "../../../constants/layout";

const EMPTY_FORM: TaskFormData = { title: "", description: "", assignee: "", assigneeEmail: null, dueDate: null };

function TranscriptStatusBadge({ status }: { status: string }) {
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
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${colorMap[status] ?? "bg-gray-100 text-gray-800"}`}>
      {iconMap[status]}
      {labelMap[status] ?? status}
    </span>
  );
}

interface AssigneePickerProps {
  users: MeetingUserDto[];
  value: string | null;
  onChange: (displayName: string, email: string | null) => void;
}

function AssigneePicker({ users, value, onChange }: AssigneePickerProps) {
  return (
    <select
      aria-label="Řešitel"
      value={value ?? ""}
      onChange={(e) => {
        const email = e.target.value || null;
        const user = users.find((u) => u.email === email);
        onChange(user?.displayName ?? "", email);
      }}
      className="flex-1 border border-gray-300 rounded-md px-2 py-1 text-sm"
    >
      <option value="">— vyberte řešitele —</option>
      {users.map((u) => (
        <option key={u.email} value={u.email}>
          {u.displayName}
        </option>
      ))}
    </select>
  );
}

const MeetingTaskDetailPage: React.FC = () => {
  const { id = "" } = useParams<{ id: string }>();
  const detail = useMeetingTaskDetail(id);
  const updateTask = useUpdateProposedTask();
  const updateStatus = useUpdateProposedTaskStatus();
  const addTask = useAddProposedTask();
  const submitToTodo = useSubmitToTodo();

  const [editingTaskId, setEditingTaskId] = useState<string | null>(null);
  const [editForm, setEditForm] = useState<TaskFormData>(EMPTY_FORM);
  const [addingTask, setAddingTask] = useState(false);
  const [addForm, setAddForm] = useState<TaskFormData>(EMPTY_FORM);
  const [submitOpen, setSubmitOpen] = useState(false);
  const [approveAllError, setApproveAllError] = useState<string | null>(null);
  const users = useMeetingUsers();
  const [transcriptOpen, setTranscriptOpen] = useState(false);
  const [accessModalOpen, setAccessModalOpen] = useState(false);
  const reimport = useReimportMeeting();
  const [reimportError, setReimportError] = useState<string | null>(null);
  const isMeetingManager = useMeetingManagerPermission();

  const explainSelection = useExplainSelection();
  const explainMutation = useExplainMeetingSummary();
  const [explainModalOpen, setExplainModalOpen] = useState(false);

  const handleExplain = () => {
    if (!explainSelection.selectedText) return;
    setExplainModalOpen(true);
    explainMutation.mutate({
      transcriptId: id,
      selectedText: explainSelection.selectedText,
    });
  };

  const handleCloseExplain = () => {
    setExplainModalOpen(false);
    explainMutation.reset();
  };

  const handleReimport = async () => {
    setReimportError(null);
    try {
      await reimport.mutateAsync(id);
    } catch {
      setReimportError("Reimport se nezdařil. Nahrávka pravděpodobně ještě není zpracována na straně Plaud.");
    }
  };

  if (detail.isLoading) {
    return <div className="p-8 text-gray-500">Nacitani...</div>;
  }
  const transcript = detail.data?.transcript;
  if (!transcript) {
    return <div className="p-8 text-gray-500">Zaznam nenalezen</div>;
  }

  const tasks: ProposedTaskDto[] = transcript.tasks;
  const pendingTasks = tasks.filter((t) => t.status === "Pending");
  const approvedCount = tasks.filter((t) => t.status === "Approved").length;

  const beginEdit = (t: ProposedTaskDto) => {
    setEditingTaskId(t.id);
    setEditForm({
      title: t.title,
      description: t.description,
      assignee: t.assignee,
      assigneeEmail: t.assigneeEmail,
      dueDate: t.dueDate,
    });
  };

  const cancelEdit = () => {
    setEditingTaskId(null);
    setEditForm(EMPTY_FORM);
  };

  const saveEdit = async (taskId: string) => {
    await updateTask.mutateAsync({
      transcriptId: id,
      taskId,
      data: { ...editForm, dueDate: editForm.dueDate || null },
    });
    cancelEdit();
  };

  const changeStatus = (taskId: string, status: ProposedTaskStatus) =>
    updateStatus.mutateAsync({ transcriptId: id, taskId, status });

  const approveAll = async () => {
    setApproveAllError(null);
    let failed = 0;
    for (const t of pendingTasks) {
      try {
        await updateStatus.mutateAsync({ transcriptId: id, taskId: t.id, status: "Approved" });
      } catch {
        failed++;
      }
    }
    if (failed > 0) {
      setApproveAllError(`${failed} z ${pendingTasks.length} úloh se nepodařilo schválit.`);
    }
  };

  const handleAddTask = async () => {
    await addTask.mutateAsync({
      transcriptId: id,
      data: { ...addForm, dueDate: addForm.dueDate || null },
    });
    setAddForm(EMPTY_FORM);
    setAddingTask(false);
  };

  const confirmSubmit = async () => {
    const result = await submitToTodo.mutateAsync(id);
    if (result.failedCount === 0) {
      submitToTodo.reset();
      setSubmitOpen(false);
    }
    // Failures: keep modal open so the user can read the error list rendered below.
  };

  const closeSubmitModal = () => {
    submitToTodo.reset();
    setSubmitOpen(false);
  };

  return (
    <div className="flex flex-col w-full overflow-auto" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="px-4 sm:px-6 lg:px-8 py-3">
        <Link to="/automation/meeting-tasks" className="inline-flex items-center text-sm text-indigo-700 hover:underline">
          <ArrowLeft className="w-4 h-4 mr-1" /> Zpet na seznam
        </Link>
      </div>

      <div className="px-4 sm:px-6 lg:px-8 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">{transcript.subject}</h1>
          <p className="mt-1 text-sm text-gray-600">
            {new Date(transcript.plaudCreatedAt).toLocaleString("cs-CZ")} · {transcript.plaudRecordingId}
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <TranscriptStatusBadge status={transcript.status} />
          {transcript.accessLevel && (
            <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
              transcript.accessLevel === 'Public'
                ? 'bg-green-100 text-green-800'
                : transcript.accessLevel === 'Restricted'
                ? 'bg-yellow-100 text-yellow-800'
                : 'bg-gray-100 text-gray-600'
            }`}>
              {transcript.accessLevel === 'Private' && 'Soukromé'}
              {transcript.accessLevel === 'Public' && 'Veřejné'}
              {transcript.accessLevel === 'Restricted' && 'Omezené'}
            </span>
          )}
          {transcript.summary?.trim() && (
            <button
              type="button"
              onClick={() =>
                downloadTextFile(
                  transcript.summary ?? '',
                  `${sanitizeFilename(transcript.subject ?? '')}-summary.md`,
                  'text/markdown',
                )
              }
              className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
            >
              <Download className="w-4 h-4 mr-1" aria-hidden="true" />
              Stáhnout souhrn
            </button>
          )}
          {transcript.rawTranscript?.trim() && (
            <button
              type="button"
              onClick={() =>
                downloadTextFile(
                  transcript.rawTranscript ?? '',
                  `${sanitizeFilename(transcript.subject ?? '')}-transcript.txt`,
                  'text/plain',
                )
              }
              className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
            >
              <Download className="w-4 h-4 mr-1" aria-hidden="true" />
              Stáhnout přepis
            </button>
          )}
          <button
            type="button"
            onClick={handleReimport}
            disabled={reimport.isPending}
            className="inline-flex items-center px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <RefreshCw className={`w-4 h-4 mr-1 ${reimport.isPending ? 'animate-spin' : ''}`} />
            Reimport
          </button>
          {isMeetingManager && (
            <button
              onClick={() => setAccessModalOpen(true)}
              className="px-3 py-1 text-sm rounded-lg border border-gray-300 hover:bg-gray-50"
            >
              Spravovat přístup
            </button>
          )}
        </div>
      </div>

      {reimportError && (
        <div className="px-4 sm:px-6 lg:px-8 mt-2">
          <p className="text-sm text-red-600">{reimportError}</p>
        </div>
      )}

      <div className="px-4 sm:px-6 lg:px-8 mt-4">
        <div
          data-explainable="true"
          className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900 prose prose-sm prose-blue max-w-none"
        >
          <ReactMarkdown remarkPlugins={[remarkGfm]}>{transcript.summary}</ReactMarkdown>
        </div>
      </div>

      <div className="px-4 sm:px-6 lg:px-8 mt-3">
        <button
          type="button"
          aria-expanded={transcriptOpen}
          onClick={() => setTranscriptOpen((v) => !v)}
          className="inline-flex items-center text-sm font-medium text-gray-700 hover:text-gray-900"
        >
          {transcriptOpen ? (
            <ChevronDown className="w-4 h-4 mr-1" />
          ) : (
            <ChevronRight className="w-4 h-4 mr-1" />
          )}
          Přepis schůzky
        </button>
        {transcriptOpen && (
          <div className="mt-2 rounded-md border border-gray-200 bg-gray-50 p-3 text-sm text-gray-800 whitespace-pre-wrap max-h-96 overflow-auto">
            {transcript.rawTranscript}
          </div>
        )}
      </div>

      <div className="px-4 sm:px-6 lg:px-8 mt-6 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-gray-900">
          Navrhovane ulohy ({tasks.length})
        </h2>
        <div className="flex gap-2">
          {pendingTasks.length > 0 && (
            <button
              type="button"
              onClick={approveAll}
              className="inline-flex items-center px-3 py-1.5 rounded-md text-sm font-medium bg-green-600 text-white hover:bg-green-700"
            >
              <CheckCheck className="w-4 h-4 mr-1" /> Schvalit vse ({pendingTasks.length})
            </button>
          )}
          <button
            type="button"
            onClick={() => setAddingTask((v) => !v)}
            className="inline-flex items-center px-3 py-1.5 rounded-md text-sm font-medium bg-white text-gray-700 border border-gray-300 hover:bg-gray-50"
          >
            <Plus className="w-4 h-4 mr-1" /> Pridat ulohu
          </button>
        </div>
      </div>

      {approveAllError && (
        <div className="px-4 sm:px-6 lg:px-8 mt-2">
          <p className="text-sm text-red-600">{approveAllError}</p>
        </div>
      )}

      {addingTask && (
        <div className="px-4 sm:px-6 lg:px-8 mt-3">
          <div className="bg-white border border-gray-200 rounded-md p-3 space-y-2">
            <input
              type="text"
              placeholder="Nazev ulohy"
              value={addForm.title}
              onChange={(e) => setAddForm({ ...addForm, title: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
            />
            <textarea
              placeholder="Popis"
              value={addForm.description}
              onChange={(e) => setAddForm({ ...addForm, description: e.target.value })}
              className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
            />
            <div className="flex gap-2">
              <AssigneePicker
                users={users.data ?? []}
                value={addForm.assigneeEmail}
                onChange={(displayName, email) =>
                  setAddForm({ ...addForm, assignee: displayName, assigneeEmail: email })
                }
              />
              <input
                type="date"
                value={addForm.dueDate ?? ""}
                onChange={(e) => setAddForm({ ...addForm, dueDate: e.target.value || null })}
                className="border border-gray-300 rounded-md px-2 py-1 text-sm"
              />
            </div>
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => { setAddingTask(false); setAddForm(EMPTY_FORM); }}
                className="px-2 py-1 text-sm text-gray-700 hover:underline"
              >
                Zrusit
              </button>
              <button
                type="button"
                onClick={handleAddTask}
                disabled={!addForm.title || !addForm.assigneeEmail || addTask.isPending}
                className="px-3 py-1 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                Pridat
              </button>
            </div>
          </div>
        </div>
      )}

      <div data-explainable="true" className="px-4 sm:px-6 lg:px-8 mt-3 space-y-2">
        {tasks.map((t) => {
          const isEditing = editingTaskId === t.id;
          const cardClass = t.status === "Approved"
            ? "bg-green-50 border-green-200"
            : t.status === "Rejected"
              ? "bg-gray-50 border-gray-200 opacity-60"
              : "bg-white border-gray-200";
          return (
            <div key={t.id} className={`border rounded-md p-3 ${cardClass}`}>
              {!isEditing ? (
                <div
                  className="flex justify-between gap-3"
                  onClick={() => { if (t.status === "Pending") beginEdit(t); }}
                >
                  <div className={t.status === "Pending" ? "cursor-pointer flex-1" : "flex-1"}>
                    <div className={`font-medium ${t.status === "Rejected" ? "line-through" : ""}`}>
                      {t.title}
                      {t.isManuallyAdded && (
                        <span className="ml-2 text-xs text-gray-500">(rucne pridano)</span>
                      )}
                      {t.externalTaskId && (
                        <span className="ml-2 text-xs text-green-700">(odeslano do TODO)</span>
                      )}
                    </div>
                    {t.description && (
                      <div className="text-sm text-gray-700 mt-1 prose prose-sm max-w-none">
                        <ReactMarkdown remarkPlugins={[remarkGfm]}>{t.description}</ReactMarkdown>
                      </div>
                    )}
                    <div className="text-xs text-gray-500 mt-1 flex items-center gap-1">
                      <span>
                        {t.assignee}{t.dueDate ? ` · ${new Date(t.dueDate).toLocaleDateString("cs-CZ")}` : ""}
                      </span>
                      {!t.assigneeEmail && (
                        <span className="inline-flex items-center text-amber-700 bg-amber-100 rounded-full px-1.5 py-0.5">
                          <AlertTriangle className="w-3 h-3 mr-1" /> neznámý uživatel
                        </span>
                      )}
                    </div>
                  </div>
                  {t.status === "Pending" && (
                    <div className="flex gap-1 shrink-0">
                      <button
                        type="button"
                        title="Schvalit"
                        onClick={(e) => { e.stopPropagation(); changeStatus(t.id, "Approved"); }}
                        className="p-1 rounded-md text-green-700 hover:bg-green-100"
                      >
                        <Check className="w-4 h-4" />
                      </button>
                      <button
                        type="button"
                        title="Zamitnout"
                        onClick={(e) => { e.stopPropagation(); changeStatus(t.id, "Rejected"); }}
                        className="p-1 rounded-md text-red-700 hover:bg-red-100"
                      >
                        <X className="w-4 h-4" />
                      </button>
                    </div>
                  )}
                </div>
              ) : (
                <div className="space-y-2">
                  <input
                    type="text"
                    value={editForm.title}
                    onChange={(e) => setEditForm({ ...editForm, title: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
                  />
                  <textarea
                    value={editForm.description}
                    onChange={(e) => setEditForm({ ...editForm, description: e.target.value })}
                    className="w-full border border-gray-300 rounded-md px-2 py-1 text-sm"
                  />
                  <div className="flex gap-2">
                    <AssigneePicker
                      users={users.data ?? []}
                      value={editForm.assigneeEmail}
                      onChange={(displayName, email) =>
                        setEditForm({ ...editForm, assignee: displayName, assigneeEmail: email })
                      }
                    />
                    <input
                      type="date"
                      value={editForm.dueDate ?? ""}
                      onChange={(e) => setEditForm({ ...editForm, dueDate: e.target.value || null })}
                      className="border border-gray-300 rounded-md px-2 py-1 text-sm"
                    />
                  </div>
                  <div className="flex justify-end gap-2">
                    <button type="button" onClick={cancelEdit} className="px-2 py-1 text-sm text-gray-700 hover:underline">
                      Zrusit
                    </button>
                    <button
                      type="button"
                      onClick={() => saveEdit(t.id)}
                      disabled={updateTask.isPending}
                      className="px-3 py-1 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50"
                    >
                      Ulozit
                    </button>
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      <div className="sticky bottom-0 mt-6 px-4 sm:px-6 lg:px-8 py-3 bg-white border-t border-gray-200 flex justify-end">
        <button
          type="button"
          disabled={approvedCount === 0}
          onClick={() => setSubmitOpen(true)}
          className="inline-flex items-center px-4 py-2 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
        >
          <Send className="w-4 h-4 mr-1" /> Odeslat do TODO ({approvedCount})
        </button>
      </div>

      {submitOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-lg shadow-lg p-5 max-w-md w-full">
            <h3 className="text-lg font-semibold text-gray-900">Odeslat schvalene ulohy do Microsoft TODO?</h3>
            <p className="text-sm text-gray-600 mt-2">
              Odeslete se schvalene ulohy ({approvedCount}). Tato akce je nevratna.
            </p>
            {submitToTodo.isError && (
              <p className="text-sm text-red-600 mt-2">
                Odeslani selhalo: {submitToTodo.error instanceof Error ? submitToTodo.error.message : String(submitToTodo.error)}
              </p>
            )}
            {submitToTodo.data && submitToTodo.data.failedCount > 0 && (
              <div className="mt-3 p-3 rounded border border-red-200 bg-red-50">
                <p className="text-sm font-medium text-red-800">
                  Odeslání selhalo: {submitToTodo.data.failedCount} z {submitToTodo.data.successCount + submitToTodo.data.failedCount} úloh.
                </p>
                {submitToTodo.data.errors.length > 0 && (
                  <ul className="mt-2 list-disc list-inside text-sm text-red-700 space-y-1">
                    {submitToTodo.data.errors.map((err, i) => (
                      <li key={i}>{err}</li>
                    ))}
                  </ul>
                )}
                {submitToTodo.data.errors.some(e => e.includes('consent required') || e.includes('Tasks.ReadWrite')) && (
                  <p className="mt-2 text-sm text-red-700">
                    Microsoft 365 vyžaduje souhlas administrátora pro práci s úkoly v Planneru. Odhlaste se a znovu se přihlaste po udělení souhlasu.
                  </p>
                )}
              </div>
            )}
            <div className="flex justify-end gap-2 mt-4">
              <button
                type="button"
                onClick={closeSubmitModal}
                disabled={submitToTodo.isPending}
                className="px-3 py-1.5 rounded-md text-sm text-gray-700 hover:bg-gray-100"
              >
                Zrusit
              </button>
              <button
                type="button"
                onClick={confirmSubmit}
                disabled={submitToTodo.isPending}
                className="px-3 py-1.5 rounded-md text-sm font-medium bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50"
              >
                {submitToTodo.isPending ? "Odesilam..." : "Odeslat"}
              </button>
            </div>
          </div>
        </div>
      )}

      {explainSelection.selectedText && explainSelection.anchorRect && (
        <ExplainTooltip
          anchorRect={explainSelection.anchorRect}
          onExplain={handleExplain}
        />
      )}

      <ExplainModal
        isOpen={explainModalOpen}
        onClose={handleCloseExplain}
        isLoading={explainMutation.isPending}
        relevantTranscript={explainMutation.data?.relevantTranscript ?? null}
        explanation={explainMutation.data?.explanation ?? null}
        error={
          explainMutation.isError
            ? (explainMutation.error instanceof Error ? explainMutation.error.message : 'Chyba při načítání vysvětlení.')
            : (!explainMutation.data?.success && explainMutation.data)
              ? 'Nepodařilo se získat vysvětlení.'
              : null
        }
      />

      {isMeetingManager && transcript && (
        <ManageAccessModal
          isOpen={accessModalOpen}
          onClose={() => setAccessModalOpen(false)}
          transcript={transcript}
          users={users.data ?? []}
        />
      )}
    </div>
  );
};

// Ensure module is treated as referencing exported types (silences ts unused-import warnings for narrow types):
export type _MeetingTaskDetailTypes = TranscriptStatus;

export default MeetingTaskDetailPage;
