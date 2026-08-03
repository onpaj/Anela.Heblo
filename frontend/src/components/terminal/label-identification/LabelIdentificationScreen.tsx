import React, { useRef, useState } from "react";
import { Camera, RotateCcw, X, ZoomIn } from "lucide-react";
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

// Reference label artwork is committed as static frontend assets (one PNG per family,
// rendered offline by scripts/render-label-references.sh). These are same-origin public
// assets, so they use PUBLIC_URL directly — unlike API hooks, which must use apiClient.baseUrl.
const PUBLIC_URL = process.env.PUBLIC_URL ?? "";

// Long ingredient lists span two PDF pages, so a family may have a second image
// ({family}-2.png); single-page families 404 it and the <img> hides itself via onError.
function referenceLabelSrc(family: string, page = 1): string {
  const suffix = page > 1 ? `-${page}` : "";
  return `${PUBLIC_URL}/label-references/${family}${suffix}.png`;
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
  const [zoomFamily, setZoomFamily] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const identify = useIdentifyLabelMutation();

  const reset = () => {
    setState({ kind: "capture" });
    setSelectedFamily(null);
    setZoomFamily(null);
    if (inputRef.current) inputRef.current.value = "";
  };

  // The reference-label zoom overlay is shared across the result branches; render it
  // alongside whichever branch is active so any label image can be tapped to enlarge.
  const withZoom = (content: React.ReactNode) => (
    <>
      {content}
      {zoomFamily && (
        <ReferenceLabelViewer family={zoomFamily} onClose={() => setZoomFamily(null)} />
      )}
    </>
  );

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
    // Family is the first six characters of the product code (KRE005015 -> KRE005).
    const family = (state.variant.productCode ?? "").slice(0, 6);
    return withZoom(
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
        {family && (
          <ReferenceLabelButton family={family} onZoom={() => setZoomFamily(family)} />
        )}
        <ScanAgain onClick={reset} />
      </Centered>,
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
      return withZoom(
        <Centered>
          <p className="text-3xl font-extrabold text-neutral-slate dark:text-graphite-text">
            {selectedFamily.family ?? ""}
          </p>
          {selectedFamily.family && (
            <ReferenceLabelButton
              family={selectedFamily.family}
              onZoom={() => setZoomFamily(selectedFamily.family ?? null)}
            />
          )}
          <p className="mt-6 mb-6 text-base text-neutral-gray dark:text-graphite-muted">
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
        </Centered>,
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

    return withZoom(
      <Centered>
        <p className="mb-4 text-base text-neutral-gray dark:text-graphite-muted">
          Vyberte produkt
        </p>
        <div className="grid w-full max-w-md gap-3">
          {candidatesList.map((candidate) => {
            const family = candidate.family ?? "";
            return (
              // The zoom control is a sibling of the select button, not nested inside it —
              // nesting interactive elements is invalid and breaks tap handling.
              <div key={family} className="relative">
                <button
                  data-testid={`label-candidate-${family}`}
                  onClick={() => selectCandidate(candidate)}
                  className="flex w-full flex-col rounded-2xl border border-border-light bg-white p-5 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
                >
                  {family && (
                    <ReferenceLabelImage
                      family={family}
                      className="mb-3 h-40 w-full rounded-lg bg-white object-contain"
                    />
                  )}
                  <div className="flex items-baseline justify-between">
                    <p className="text-xl font-bold text-neutral-slate dark:text-graphite-text">
                      {family}
                    </p>
                    <span className="text-sm text-neutral-gray dark:text-graphite-muted">
                      {(candidate.score ?? 0).toFixed(1)}
                    </span>
                  </div>
                  <p className="text-sm text-neutral-gray dark:text-graphite-muted">
                    {(candidate.variants ?? [])
                      .map((v) => v.productName)
                      .filter(Boolean)
                      .join(" / ")}
                  </p>
                </button>
                {family && (
                  <button
                    type="button"
                    aria-label="Zvětšit štítek"
                    data-testid={`label-candidate-zoom-${family}`}
                    onClick={() => setZoomFamily(family)}
                    className="absolute right-3 top-3 rounded-full bg-white/90 p-2 text-neutral-slate shadow dark:bg-graphite-surface dark:text-graphite-text"
                  >
                    <ZoomIn className="h-5 w-5" />
                  </button>
                )}
              </div>
            );
          })}
        </div>
        <ScanAgain onClick={reset} label="Skenovat další" />
      </Centered>,
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

// The reference label artwork for a family; hides itself if the PNG is missing (e.g. a
// family whose artwork hasn't been rendered yet) so a broken image never reaches the floor.
const ReferenceLabelImage: React.FC<{ family: string; page?: number; className?: string }> = ({
  family,
  page = 1,
  className,
}) => {
  const [hasError, setHasError] = useState(false);
  if (hasError) return null;
  return (
    <img
      src={referenceLabelSrc(family, page)}
      alt={`Referenční štítek ${family}`}
      onError={() => setHasError(true)}
      className={className}
    />
  );
};

// Standalone tap-to-zoom label used on the size-step and final screens. Hides itself (and
// its "zvětšit" hint) if the artwork is missing, rather than leaving an empty tap target.
const ReferenceLabelButton: React.FC<{ family: string; onZoom: () => void }> = ({
  family,
  onZoom,
}) => {
  const [hasError, setHasError] = useState(false);
  if (hasError) return null;
  return (
    <button
      type="button"
      onClick={onZoom}
      data-testid={`label-reference-${family}`}
      className="mt-6 flex flex-col items-center gap-1"
    >
      <img
        src={referenceLabelSrc(family)}
        alt={`Referenční štítek ${family}`}
        onError={() => setHasError(true)}
        className="h-56 w-56 rounded-xl bg-white object-contain shadow-soft"
      />
      <span className="flex items-center gap-1 text-sm text-neutral-gray dark:text-graphite-muted">
        <ZoomIn className="h-4 w-4" /> Klepnutím zvětšíte
      </span>
    </button>
  );
};

// Fullscreen reader so the operator can read the INCI text and confirm the match. Shows
// page 1 and (for long ingredient lists) page 2; the backdrop and close button dismiss it.
const ReferenceLabelViewer: React.FC<{ family: string; onClose: () => void }> = ({
  family,
  onClose,
}) => (
  <div
    role="dialog"
    aria-modal="true"
    data-testid="label-reference-viewer"
    onClick={onClose}
    className="fixed inset-0 z-50 flex flex-col items-center gap-4 overflow-y-auto bg-black/80 p-4"
  >
    <button
      type="button"
      onClick={onClose}
      aria-label="Zavřít"
      className="sticky top-0 self-end rounded-full bg-white/90 p-2 text-neutral-slate shadow"
    >
      <X className="h-6 w-6" />
    </button>
    <div className="flex flex-col items-center gap-4" onClick={(e) => e.stopPropagation()}>
      <ReferenceLabelImage
        family={family}
        page={1}
        className="max-h-[80vh] w-auto rounded-xl bg-white"
      />
      <ReferenceLabelImage
        family={family}
        page={2}
        className="max-h-[80vh] w-auto rounded-xl bg-white"
      />
    </div>
  </div>
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
