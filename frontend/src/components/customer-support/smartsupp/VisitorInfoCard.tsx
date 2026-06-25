import React, { useState } from "react";
import { useSmartsuppVisitorInfo } from "../../../api/hooks/useSmartsupp";
import Section from "./Section";

const INITIAL_PAGE_LIMIT = 3;

interface VisitorInfoCardProps {
  conversationId: string | null;
}

function buildDeviceLabel(
  browser: string | null | undefined,
  browserVersion: string | null | undefined,
  os: string | null | undefined
): string | null {
  const browserStr = browser && browserVersion ? `${browser} ${browserVersion}` : browser ?? null;
  if (browserStr && os) return `${browserStr}, ${os}`;
  return browserStr ?? os ?? null;
}

function VisitorInfoCard({ conversationId }: VisitorInfoCardProps) {
  const { data, isLoading } = useSmartsuppVisitorInfo(conversationId);
  const [expanded, setExpanded] = useState(false);

  if (isLoading) return null;
  if (!data?.visitorInfo) return null;

  const { os, browser, browserVersion, pages } = data.visitorInfo;
  const deviceLabel = buildDeviceLabel(browser, browserVersion, os);

  const visiblePages = expanded ? pages : pages.slice(0, INITIAL_PAGE_LIMIT);
  const hiddenCount = pages.length - INITIAL_PAGE_LIMIT;

  return (
    <>
      {deviceLabel && (
        <Section title="Zařízení">
          <div className="text-sm text-gray-700 dark:text-graphite-muted">{deviceLabel}</div>
        </Section>
      )}

      {pages.length > 0 && (
        <Section title="Historie procházení">
          <div className="space-y-1">
            {visiblePages.map((p, i) => (
              <a
                key={i}
                href={p.url}
                target="_blank"
                rel="noopener noreferrer"
                className="block text-xs text-blue-600 dark:text-blue-400 hover:underline truncate"
                title={p.url}
              >
                {p.url}
              </a>
            ))}
            {!expanded && hiddenCount > 0 && (
              <button
                onClick={() => setExpanded(true)}
                className="text-xs text-gray-500 dark:text-graphite-muted hover:text-gray-700 mt-0.5"
              >
                + {hiddenCount} stránky
              </button>
            )}
          </div>
        </Section>
      )}
    </>
  );
}

export default VisitorInfoCard;
