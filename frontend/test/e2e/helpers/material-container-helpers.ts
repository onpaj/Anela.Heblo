import { Page, expect } from '@playwright/test';

/**
 * Helpers for the terminal "Identifikace šarže" (receive materials with lots) E2E flow.
 *
 * The receive flow can only *assign* container codes that already exist as `Unassigned`
 * (the backend rejects unknown codes). Real Unassigned containers are minted by the
 * `print-labels` endpoint. On Staging the physical print is skipped via the
 * `is-label-printing-enabled` feature flag, so this endpoint is a clean, side-effect-free
 * way to provision fresh codes per run.
 *
 * All calls go through `page.request`, which shares the browser context's cookie jar — so
 * after `navigateToApp(page)` they authenticate with the same E2E session cookie the app uses.
 */

export interface SeededContainer {
  id: number;
  code: string;
  status: string;
}

/** Mints `count` fresh Unassigned material containers and returns them (with server-generated codes). */
export async function seedUnassignedContainers(page: Page, count: number): Promise<SeededContainer[]> {
  const response = await page.request.post('/api/material-containers/print-labels', {
    // mediaChangeConfirmed avoids the media-change confirmation short-circuit; with label
    // printing disabled on Staging the print is skipped but the containers are still created.
    data: { count, mediaChangeConfirmed: true },
  });
  expect(
    response.ok(),
    `print-labels failed (${response.status()}): ${await response.text()}`,
  ).toBeTruthy();

  const body = await response.json();
  const containers: SeededContainer[] = body.containers ?? [];
  expect(
    containers.length,
    `Expected ${count} seeded containers, got ${containers.length}`,
  ).toBe(count);
  return containers;
}

/** Reads a single material container by its code (returns undefined when not found). */
export async function getContainerByCode(page: Page, code: string) {
  const response = await page.request.get(
    `/api/material-containers?code=${encodeURIComponent(code)}`,
  );
  expect(response.ok(), `list by code failed (${response.status()})`).toBeTruthy();
  const body = await response.json();
  const containers = body.containers ?? [];
  return containers.find((c: { code: string }) => c.code === code);
}

/** Discards the given containers so a run leaves no residual Assigned data in staging. */
export async function discardContainers(page: Page, ids: number[]): Promise<void> {
  for (const id of ids) {
    // Best-effort cleanup — don't fail the test if a discard call errors.
    await page.request.post(`/api/material-containers/${id}/discard`).catch(() => undefined);
  }
}
