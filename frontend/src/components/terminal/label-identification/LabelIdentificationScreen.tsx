import React, { useRef, useState } from "react";
import { Camera, RotateCcw } from "lucide-react";
import { useScreenView } from "../../../telemetry/useScreenView";
import { useIdentifyLabelMutation } from "../../../api/hooks/useLabelIdentification";
import {
  IdentifyLabelResponse,
  LabelCandidateDto,
  LabelMatchDecision,
  LabelVariantDto,
} from "../../../api/generated/api-client";
import { handleApiError } from "../../../utils/errorHandler";

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

  const handlePhoto = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;

    try {
      const response = await identify.mutateAsync(file);
      if (!response.success) {
        // response.success is `boolean | undefined` on the generated
        // IdentifyLabelResponse (it extends the abstract BaseResponse class),
        // while handleApiError's local BaseResponse requires `success: boolean`.
        // Build an explicit literal rather than widening the shared type.
        setState({
          kind: "error",
          message: handleApiError({
            success: false,
            errorCode: response.errorCode,
            params: response.params,
          }),
        });
        return;
      }
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
    } catch {
      // Thrown/network failure — there is no BaseResponse to inspect here (no
      // errorCode from the server), so we can't route this through
      // handleApiError/i18n the way the response.success === false branch
      // above does. This literal mirrors the other hardcoded UI copy in this
      // component (e.g. the Low-decision message below) rather than faking a
      // BaseResponse just to reach the generic, differently-worded
      // errors.ExternalServiceError string shared by every other module.
      setState({
        kind: "error",
        message: "Služba rozpoznávání není dostupná, zkuste to znovu.",
      });
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
          {state.variant.productCode}
        </p>
        <p className="mt-3 text-xl text-neutral-slate dark:text-graphite-text">
          {state.variant.productName}
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

    if (selectedFamily) {
      return (
        <Centered>
          <p className="text-3xl font-extrabold text-neutral-slate dark:text-graphite-text">
            {selectedFamily.family}
          </p>
          <p className="mt-2 mb-6 text-base text-neutral-gray dark:text-graphite-muted">
            Vyberte velikost
          </p>
          <div data-testid="label-size-step" className="grid w-full max-w-md gap-4">
            {(selectedFamily.variants ?? []).map((variant) => (
              <button
                key={variant.productCode}
                data-testid={`label-variant-${variant.productCode}`}
                onClick={() => setState({ kind: "chosen", variant })}
                className="rounded-2xl border border-border-light bg-white p-6 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
              >
                <p className="text-2xl font-bold text-neutral-slate dark:text-graphite-text">
                  {variant.productCode}
                </p>
                <p className="text-sm text-neutral-gray dark:text-graphite-muted">
                  {variant.productName}
                </p>
              </button>
            ))}
          </div>
          <ScanAgain onClick={reset} />
        </Centered>
      );
    }

    const isLow = response.decision === LabelMatchDecision.Low;
    return (
      <Centered>
        {isLow && (
          <p className="mb-4 text-lg font-semibold text-rose-600 dark:text-rose-400">
            Nepodařilo se přečíst štítek
          </p>
        )}
        {!isLow && (
          <p className="mb-4 text-base text-neutral-gray dark:text-graphite-muted">
            Vyberte produkt
          </p>
        )}
        <div className="grid w-full max-w-md gap-3">
          {(response.candidates ?? []).map((candidate) => (
            <button
              key={candidate.family}
              data-testid={`label-candidate-${candidate.family}`}
              onClick={() => setSelectedFamily(candidate)}
              className="rounded-2xl border border-border-light bg-white p-5 text-left shadow-soft transition-all hover:border-primary-blue dark:border-graphite-border dark:bg-graphite-surface"
            >
              <div className="flex items-baseline justify-between">
                <p className="text-xl font-bold text-neutral-slate dark:text-graphite-text">
                  {candidate.family}
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
        <ScanAgain onClick={reset} label={isLow ? "Zkusit znovu" : "Skenovat další"} />
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
