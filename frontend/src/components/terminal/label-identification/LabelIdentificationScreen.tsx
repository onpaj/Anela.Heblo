import React, { useRef, useState } from "react";
import { Camera, RotateCcw } from "lucide-react";
import { useScreenView } from "../../../telemetry/useScreenView";
import { useIdentifyLabelMutation } from "../../../api/hooks/useLabelIdentification";
import {
  ErrorCodes,
  IdentifyLabelResponse,
  LabelCandidateDto,
  LabelMatchDecision,
  LabelVariantDto,
  SwaggerException,
} from "../../../api/generated/api-client";
import { handleApiError } from "../../../utils/errorHandler";

// Kestrel rejects an oversized body before the controller runs, so there is no JSON
// response at all for a 413 — the spec maps oversized uploads to the same code as a
// missing/non-image photo.
const HTTP_PAYLOAD_TOO_LARGE = 413;

/**
 * The generated client throws SwaggerException for any non-200/204 response, discarding
 * nothing — `error.response` is the raw response body text. All four label error codes
 * (3301-3304) map to non-200 statuses, so this is the only place a failed identify call's
 * real errorCode can be recovered; without it every failure looks like a generic OCR outage.
 */
function resolveIdentifyErrorMessage(error: unknown): string {
  if (error instanceof SwaggerException) {
    const parsedBody = parseFailedIdentifyResponse(error.response);
    if (parsedBody) {
      return handleApiError({
        success: false,
        errorCode: parsedBody.errorCode,
        params: parsedBody.params,
      });
    }
    if (error.status === HTTP_PAYLOAD_TOO_LARGE) {
      return handleApiError({
        success: false,
        errorCode: ErrorCodes.LabelPhotoMissingOrInvalid,
      });
    }
  }

  // Parse failure, network error, or anything else that isn't a structured server
  // response — LabelOcrServiceUnavailable is a dedicated error code (not the generic,
  // differently-worded errors.ExternalServiceError shared by every other module) specifically
  // so this feature-specific message lives in i18n like every other ErrorCodes member,
  // rather than as a component-local literal.
  return handleApiError({
    success: false,
    errorCode: ErrorCodes.LabelOcrServiceUnavailable,
  });
}

/**
 * The response body may be empty, HTML (e.g. a proxy error page), or a ProblemDetails
 * object instead of the serialized IdentifyLabelResponse we expect — parse defensively.
 */
function parseFailedIdentifyResponse(
  responseBody: string,
): { errorCode: ErrorCodes; params?: Record<string, string> } | undefined {
  try {
    const parsed = JSON.parse(responseBody);
    if (
      parsed &&
      typeof parsed === "object" &&
      parsed.success === false &&
      typeof parsed.errorCode === "string"
    ) {
      return { errorCode: parsed.errorCode, params: parsed.params };
    }
  } catch {
    // Not JSON — fall through to the generic fallback.
  }
  return undefined;
}

type ScreenState =
  | { kind: "capture" }
  | { kind: "result"; response: IdentifyLabelResponse }
  | { kind: "chosen"; variant: LabelVariantDto }
  | { kind: "error"; message: string };

const LabelIdentificationScreen: React.FC = () => {
  useScreenView("Terminal", "LabelIdentification");

  const [state, setState] = useState<ScreenState>({ kind: "capture" });
  const [selectedFamily, setSelectedFamily] = useState<LabelCandidateDto | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const identify = useIdentifyLabelMutation();

  const reset = () => {
    setState({ kind: "capture" });
    setSelectedFamily(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  // A family with exactly one variant needs no size step, whether it's the operator
  // picking from a Choose decision's candidate list or (handled separately above) an
  // Auto decision's single top match.
  const selectCandidate = (candidate: LabelCandidateDto) => {
    const variants = candidate.variants ?? [];
    if (variants.length === 1) {
      setState({ kind: "chosen", variant: variants[0] });
      return;
    }
    setSelectedFamily(candidate);
  };

  const handlePhoto = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    try {
      const response = await identify.mutateAsync(file);
      // A family with exactly one variant needs no size step.
      const top = response.candidates?.[0];
      const topVariants = top?.variants ?? [];
      if (response.decision === LabelMatchDecision.Auto && topVariants.length === 1) {
        setState({ kind: "chosen", variant: topVariants[0] });
        return;
      }
      if (response.decision === LabelMatchDecision.Auto && top) {
        setSelectedFamily(top);
      }
      setState({ kind: "result", response });
    } catch (error: unknown) {
      // The generated client throws for any non-200/204 status, and all four label
      // error codes map to non-200 statuses — so a failed identify call never resolves
      // with a `BaseResponse` here. The real errorCode (if any) has to be recovered
      // from the thrown error instead.
      setState({ kind: "error", message: resolveIdentifyErrorMessage(error) });
    }
  };

  if (identify.isPending) {
    return (
      <Centered>
        <div className="h-12 w-12 animate-spin rounded-full border-4 border-primary-blue border-t-transparent" />
        <p className="mt-4 text-lg text-neutral-slate dark:text-graphite-text">Čtu štítek…</p>
      </Centered>
    );
  }

  if (state.kind === "chosen") {
    return (
      <Centered>
        <p
          data-testid="label-final-code"
          className="text-5xl font-extrabold text-emerald-600 dark:text-emerald-400"
        >
          {state.variant.productCode || "—"}
        </p>
        <p className="mt-3 text-xl text-neutral-slate dark:text-graphite-text">
          {state.variant.productName ?? ""}
        </p>
        <ScanAgain onClick={reset} />
      </Centered>
    );
  }

  if (state.kind === "error") {
    return (
      <Centered>
        <p className="text-lg font-semibold text-rose-600 dark:text-rose-400">{state.message}</p>
        <ScanAgain onClick={reset} label="Zkusit znovu" />
      </Centered>
    );
  }

  if (state.kind === "result") {
    const { response } = state;
    const candidatesList = response.candidates ?? [];

    if (selectedFamily) {
      const variants = selectedFamily.variants ?? [];
      // Defensive: the backend should always ship at least one variant per
      // candidate. If it somehow doesn't, a size step with zero buttons is a
      // dead end — fall back to the same unreadable-label failure state used
      // for a Low decision rather than leaving the operator stuck.
      if (variants.length === 0) {
        return <UnreadableLabel onRetry={reset} />;
      }
      return (
        <Centered>
          <p className="text-3xl font-extrabold text-neutral-slate dark:text-graphite-text">
            {selectedFamily.family ?? ""}
          </p>
          <p className="mt-2 mb-6 text-base text-neutral-gray dark:text-graphite-muted">
            Vyberte velikost
          </p>
          <div data-testid="label-size-step" className="grid w-full max-w-md gap-4">
            {variants.map((variant) => (
              <button
                key={variant.productCode ?? "—"}
                data-testid={`label-variant-${variant.productCode ?? "—"}`}
                onClick={() => setState({ kind: "chosen", variant })}
                className="rounded-2xl border border-border-light bg-white p-6 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
              >
                <p className="text-2xl font-bold text-neutral-slate dark:text-graphite-text">
                  {variant.productCode || "—"}
                </p>
                <p className="text-sm text-neutral-gray dark:text-graphite-muted">
                  {variant.productName ?? ""}
                </p>
              </button>
            ))}
          </div>
          <ScanAgain onClick={reset} />
        </Centered>
      );
    }

    // Defensive: an empty candidate list is treated the same as a Low decision
    // — the backend should never send one for a non-Low decision, but a blank
    // "Vyberte produkt" screen with nothing under it is the worst failure mode
    // on a warehouse floor.
    const isLow = response.decision === LabelMatchDecision.Low || candidatesList.length === 0;
    if (isLow) {
      return <UnreadableLabel onRetry={reset} />;
    }

    return (
      <Centered>
        <p className="mb-4 text-base text-neutral-gray dark:text-graphite-muted">
          Vyberte produkt
        </p>
        <div className="grid w-full max-w-md gap-3">
          {candidatesList.map((candidate) => (
            <button
              key={candidate.family ?? ""}
              data-testid={`label-candidate-${candidate.family ?? ""}`}
              onClick={() => selectCandidate(candidate)}
              className="rounded-2xl border border-border-light bg-white p-5 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
            >
              <div className="flex items-baseline justify-between">
                <p className="text-xl font-bold text-neutral-slate dark:text-graphite-text">
                  {candidate.family ?? ""}
                </p>
                <span className="text-sm text-neutral-gray dark:text-graphite-muted">
                  {(candidate.score ?? 0).toFixed(1)}
                </span>
              </div>
              <p className="text-sm text-neutral-gray dark:text-graphite-muted">
                {(candidate.variants ?? []).map((v) => v.productName).filter(Boolean).join(" / ")}
              </p>
            </button>
          ))}
        </div>
        <ScanAgain onClick={reset} label="Skenovat další" />
      </Centered>
    );
  }

  return (
    <Centered>
      <label
        htmlFor="label-photo-input"
        className="flex w-full max-w-md cursor-pointer flex-col items-center gap-3 rounded-2xl bg-primary-blue p-10 text-white shadow-lg"
      >
        <Camera className="h-12 w-12" />
        <span className="text-2xl font-bold">Vyfotit štítek</span>
      </label>
      <input
        id="label-photo-input"
        data-testid="label-photo-input"
        ref={inputRef}
        type="file"
        accept="image/*"
        capture="environment"
        className="hidden"
        onChange={handlePhoto}
      />
    </Centered>
  );
};

const Centered: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div className="flex h-full flex-col items-center justify-center p-4">{children}</div>
);

// Shared with both the Low decision and the two defensive dead-end cases
// (empty candidate list, selected family with no variants) — same failure
// state, same retry, rather than inventing a distinct message per case.
const UnreadableLabel: React.FC<{ onRetry: () => void }> = ({ onRetry }) => (
  <Centered>
    <p className="mb-4 text-lg font-semibold text-rose-600 dark:text-rose-400">
      Nepodařilo se přečíst štítek
    </p>
    <ScanAgain onClick={onRetry} label="Zkusit znovu" />
  </Centered>
);

const ScanAgain: React.FC<{ onClick: () => void; label?: string }> = ({
  onClick,
  label = "Skenovat další",
}) => (
  <button
    onClick={onClick}
    className="mt-8 inline-flex items-center gap-2 text-base font-semibold text-primary-blue dark:text-graphite-accent"
  >
    <RotateCcw className="h-4 w-4" />
    {label}
  </button>
);

export default LabelIdentificationScreen;
