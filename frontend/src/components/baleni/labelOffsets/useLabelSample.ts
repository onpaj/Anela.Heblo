import { useEffect, useRef, useState } from 'react';
import { usePackagesQuery } from '../../../api/hooks/usePackages';
import { buildProxyUrl, fetchLabelPdf } from '../printLabelPdf';
import { LabelCarrier, LabelOffset } from './labelOffsetStorage';

/** How many of the carrier's most recent packages are tried; older labels may no longer be served. */
const SAMPLE_CANDIDATES = 5;

/** Wait for the arrows to settle before re-rendering the preview. */
const PREVIEW_DEBOUNCE_MS = 250;

export interface LabelSample {
  orderCode: string;
  packageNumber: number;
}

export type LabelSampleStatus = 'loading' | 'ready' | 'none' | 'error';

export interface LabelSampleState {
  sample: LabelSample | null;
  /** Object URL of the sample label PDF with the offset applied by the backend. */
  previewUrl: string | null;
  status: LabelSampleStatus;
}

const LOADING: LabelSampleState = { sample: null, previewUrl: null, status: 'loading' };

const toSample = (orderCode: string, packageNumber: string): LabelSample | null => {
  const number = Number(packageNumber);
  return Number.isInteger(number) && number > 0 ? { orderCode, packageNumber: number } : null;
};

/** Returns the PDF blob of the first sample whose label still downloads, or null when none does. */
const findSamplePdf = async (
  samples: LabelSample[],
  carrier: LabelCarrier,
  offset: LabelOffset,
): Promise<{ sample: LabelSample; blob: Blob } | null> => {
  for (const sample of samples) {
    // Poll-marked: an old label that is gone is an expected 404, not an error worth logging.
    const response = await fetchLabelPdf(
      buildProxyUrl(sample.orderCode, sample.packageNumber, { [carrier]: offset }),
      true,
    );
    if (response.ok) return { sample, blob: await response.blob() };
    if (response.status !== 404) throw new Error(`Label download failed: ${response.status}`);
  }
  return null;
};

/**
 * Loads the most recently packed label of `carrier` that the carrier still serves and renders it
 * with `offset` applied, re-rendering (debounced) whenever the offset changes.
 */
export const useLabelSample = (carrier: LabelCarrier, offset: LabelOffset): LabelSampleState => {
  const packages = usePackagesQuery({
    carrier,
    pageNumber: 1,
    pageSize: SAMPLE_CANDIDATES,
    sortBy: 'PackedAt',
    sortDescending: true,
  });

  // Tagged with its carrier so a switch never shows (or test-prints) the previous carrier's sample.
  const [state, setState] = useState<LabelSampleState & { carrier: LabelCarrier }>({ ...LOADING, carrier });
  // Once a working sample is found for the carrier, later offset changes only re-fetch that one.
  const workingSample = useRef<{ carrier: LabelCarrier; sample: LabelSample } | null>(null);

  useEffect(() => {
    if (packages.isLoading) return;
    if (packages.error) {
      setState({ sample: null, previewUrl: null, status: 'error', carrier });
      return;
    }

    const known = workingSample.current?.carrier === carrier ? [workingSample.current.sample] : null;
    const candidates =
      known ??
      (packages.data?.items ?? []).flatMap((p) => {
        const sample = toSample(p.orderCode, p.packageNumber);
        return sample ? [sample] : [];
      });

    if (!known) setState({ ...LOADING, carrier });

    let isCancelled = false;
    let createdUrl: string | null = null;

    const timer = setTimeout(async () => {
      try {
        const found = await findSamplePdf(candidates, carrier, offset);
        if (isCancelled) return;
        if (!found) {
          workingSample.current = null;
          setState({ sample: null, previewUrl: null, status: 'none', carrier });
          return;
        }
        workingSample.current = { carrier, sample: found.sample };
        createdUrl = URL.createObjectURL(found.blob);
        setState({ sample: found.sample, previewUrl: createdUrl, status: 'ready', carrier });
      } catch {
        if (!isCancelled) setState({ sample: null, previewUrl: null, status: 'error', carrier });
      }
    }, PREVIEW_DEBOUNCE_MS);

    return () => {
      isCancelled = true;
      clearTimeout(timer);
      // Revoked when the next preview replaces it or the screen closes.
      if (createdUrl) {
        const url = createdUrl;
        setTimeout(() => URL.revokeObjectURL(url), PREVIEW_DEBOUNCE_MS * 4);
      }
    };
  }, [carrier, offset, packages.isLoading, packages.error, packages.data]);

  if (state.carrier !== carrier) return LOADING;
  const { sample, previewUrl, status } = state;
  return { sample, previewUrl, status };
};
