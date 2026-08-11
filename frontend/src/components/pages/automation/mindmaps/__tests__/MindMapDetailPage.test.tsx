import React from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createMockApiClient, mockAuthenticatedApiClient } from "../../../../../api/testUtils";
import { MIND_MAPS_KEYS } from "../../../../../api/hooks/useMindMaps";
import { MindMapDocument } from "../mindMapDocument";
import MindMapDetailPage from "../MindMapDetailPage";

jest.mock("../../../../../api/client");

// The canvas renders through @xyflow/react, which needs browser APIs (ResizeObserver
// etc.) jsdom doesn't provide. These tests are about MindMapDetailPage's own
// save/adoption/read-only logic, not the canvas's rendering, so stub it with a plain
// list of clickable node buttons that still exercise the real onSelectNode callback.
jest.mock("../MindMapCanvas", () => ({
  __esModule: true,
  default: ({
    initialDocument,
    onSelectNode,
  }: {
    initialDocument: MindMapDocument;
    onSelectNode: (id: string) => void;
  }) => (
    <div data-testid="mindmap-canvas-stub">
      {initialDocument.nodes.map((n) => (
        <button key={n.id} type="button" onClick={() => onSelectNode(n.id)}>
          {n.title}
        </button>
      ))}
    </div>
  ),
}));

const BASE_URL = "http://localhost:5000";
const MAP_ID = "map-1";
const DETAIL_URL = `${BASE_URL}/api/mind-maps/${MAP_ID}`;
const SAVE_URL = `${DETAIL_URL}/document`;

function buildDoc(overrides: Partial<MindMapDocument> = {}): MindMapDocument {
  return {
    schemaVersion: 1,
    rootNodeId: "root",
    nodes: [
      {
        id: "root",
        parentId: null,
        title: "Projekt",
        notes: null,
        status: "active",
        owner: null,
        lockedBy: null,
        sourceMeetingIds: [],
        position: null,
        collapsed: false,
      },
    ],
    suppressedNodes: [],
    ...overrides,
  };
}

function buildDetail(overrides: Record<string, unknown> = {}) {
  return {
    id: MAP_ID,
    name: "Testovací mapa",
    description: null,
    status: "Idle",
    lastError: null,
    documentJson: JSON.stringify(buildDoc()),
    meetings: [],
    versions: [],
    ...overrides,
  };
}

function jsonResponse(body: unknown) {
  return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) });
}

function newQueryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
}

function renderPage(queryClient: QueryClient) {
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[`/automation/mind-maps/${MAP_ID}`]}>
        <Routes>
          <Route path="/automation/mind-maps/:id" element={<MindMapDetailPage />} />
          <Route path="/automation/mind-maps" element={<div>SEZNAM MAP</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function selectRootNode() {
  fireEvent.click(await screen.findByRole("button", { name: "Projekt" }));
}

describe("MindMapDetailPage", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("does not let a fresher server document clobber an unsaved local edit (adoption guard)", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const initialDetail = buildDetail();
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) return jsonResponse(initialDetail);
      throw new Error(`Unexpected fetch: ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await selectRootNode();

    const titleInput = (await screen.findByTestId("mindmap-panel-title-input")) as HTMLInputElement;
    expect(titleInput.value).toBe("Projekt");

    fireEvent.change(titleInput, { target: { value: "Rozepsáno uživatelem" } });
    expect(titleInput.value).toBe("Rozepsáno uživatelem");

    // Simulate a fresher document landing in the cache (e.g. a 3s "Updating" poll,
    // or a just-finished Claude rewrite) while the user is still mid-edit. React
    // Query notifies observers a macrotask after setQueryData, not synchronously
    // within `act`'s callback — flush it before asserting, otherwise the check
    // below would run against a render that hasn't happened yet and pass
    // vacuously either way.
    await act(async () => {
      queryClient.setQueryData(MIND_MAPS_KEYS.detail(MAP_ID), {
        ...initialDetail,
        documentJson: JSON.stringify(buildDoc({ nodes: [{ ...buildDoc().nodes[0], title: "Ze serveru" }] })),
      });
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    // The adoption effect must not overwrite the user's in-progress edit.
    expect(titleInput.value).toBe("Rozepsáno uživatelem");
  });

  it("keeps the just-saved document after save, even though the invalidated refetch never lands (issue-1 regression)", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const initialDetail = buildDetail();
    const canonicalDoc = buildDoc({
      nodes: [{ ...buildDoc().nodes[0], title: "Upraveno", lockedBy: "ondra@anela.cz" }],
    });
    let getCallCount = 0;
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? "GET").toUpperCase();
      if (method === "GET" && url === DETAIL_URL) {
        getCallCount += 1;
        // First call: the initial page load. Every call after that simulates the
        // background refetch that invalidateQueries kicks off after a save —
        // deliberately left hanging, so the test isolates exactly what handleSave's
        // own cache write accomplishes, independent of whether/when that refetch
        // ever resolves.
        if (getCallCount === 1) return jsonResponse(initialDetail);
        return new Promise(() => {});
      }
      if (method === "PUT" && url === SAVE_URL) {
        return jsonResponse({ documentJson: JSON.stringify(canonicalDoc) });
      }
      throw new Error(`Unexpected fetch: ${method} ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await selectRootNode();

    const titleInput = (await screen.findByTestId("mindmap-panel-title-input")) as HTMLInputElement;
    fireEvent.change(titleInput, { target: { value: "Upraveno" } });

    fireEvent.click(screen.getByTestId("mindmap-save-button"));

    // Wait on the exact thing being asserted — the lock line only renders once the
    // canonical (post-save) document is what's displayed. `waitFor` on the save
    // button's disabled state would only prove the mutation settled, not that its
    // response was actually adopted; that happened to be true today, but coupling
    // the wait to a different signal than the assertion is a latent flake.
    await screen.findByText(/Uzamčeno uživatelem ondra@anela\.cz/);

    // With the bug, the adoption effect fires using the stale (pre-save) cached
    // `detail` as soon as isDirty flips false — reverting the title back to
    // "Projekt" and dropping the lock the server just applied. Since the refetch
    // above never resolves, an unfixed page would stay reverted for good.
    expect(titleInput.value).toBe("Upraveno");
  });

  it("shows an error state instead of crashing when the initial document JSON is malformed", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const brokenDetail = buildDetail({ documentJson: "not valid json" });
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) return jsonResponse(brokenDetail);
      throw new Error(`Unexpected fetch: ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);

    // parseDocument throws on "not valid json"; with no prior localDoc to fall
    // back on, the adoption effect must surface this as a rendered error state,
    // not let the exception propagate up into the global ErrorBoundary.
    expect(await screen.findByText(/Dokument mapy se nepodařilo načíst/)).toBeInTheDocument();
    expect(screen.queryByTestId("mindmap-canvas-stub")).not.toBeInTheDocument();
  });

  it("keeps showing a working session, with a warning, when a later poll delivers a malformed document", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const initialDetail = buildDetail();
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) return jsonResponse(initialDetail);
      throw new Error(`Unexpected fetch: ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await screen.findByTestId("mindmap-canvas-stub");

    // Simulate a later poll/refetch landing with a corrupted payload — the
    // already-loaded, working document must stay on screen (not be clobbered,
    // not crash) with a warning surfaced instead.
    await act(async () => {
      queryClient.setQueryData(MIND_MAPS_KEYS.detail(MAP_ID), {
        ...initialDetail,
        documentJson: "not valid json",
      });
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    expect(screen.getByTestId("mindmap-canvas-stub")).toBeInTheDocument();
    expect(screen.getByText(/Poslední verzi mapy ze serveru se nepodařilo načíst/)).toBeInTheDocument();
  });

  it("disables the panel and save controls while the map is Updating", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const updatingDetail = buildDetail({ status: "Updating" });
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) return jsonResponse(updatingDetail);
      throw new Error(`Unexpected fetch: ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await selectRootNode();

    expect(screen.getByTestId("mindmap-status-badge")).toHaveTextContent("Aktualizuje se");
    expect(screen.getByTestId("mindmap-save-button")).toBeDisabled();
    expect(screen.getByTestId("mindmap-panel-title-input")).toBeDisabled();
    expect(screen.getByText(/Mapa se právě aktualizuje/)).toBeInTheDocument();
  });

  it("keeps showing the editor, not the error screen, when a background refetch transiently fails", async () => {
    const { mockClient, mockFetch } = createMockApiClient(BASE_URL);
    mockAuthenticatedApiClient(mockClient);
    const initialDetail = buildDetail();
    let getCallCount = 0;
    mockFetch.mockImplementation((url: string) => {
      if (url === DETAIL_URL) {
        getCallCount += 1;
        if (getCallCount === 1) return jsonResponse(initialDetail);
        return Promise.reject(new Error("network blip"));
      }
      throw new Error(`Unexpected fetch: ${url}`);
    });

    const queryClient = newQueryClient();
    renderPage(queryClient);
    await screen.findByTestId("mindmap-canvas-stub");

    await act(async () => {
      await queryClient.refetchQueries({ queryKey: MIND_MAPS_KEYS.detail(MAP_ID) }).catch(() => undefined);
      // React Query notifies observers a macrotask after the query settles, not
      // synchronously within the awaited refetch — flush it before asserting so
      // this check reflects the component's *settled* render, not a stale one that
      // merely hasn't re-rendered yet (`waitFor` would resolve on that first,
      // still-stale render and pass vacuously either way).
      await new Promise((resolve) => setTimeout(resolve, 0));
    });

    // Sanity check: the refetch really did fail and really did retain prior data —
    // otherwise this test would pass vacuously regardless of the component's guard.
    const state = queryClient.getQueryState(MIND_MAPS_KEYS.detail(MAP_ID));
    expect(state?.error).toBeTruthy();
    expect(state?.data).toBeTruthy();

    // The retained `data` from the first successful fetch must keep the editor on
    // screen; a transient refetch failure must not fall back to the error state.
    expect(screen.getByTestId("mindmap-canvas-stub")).toBeInTheDocument();
    expect(screen.queryByText("Nepodařilo se načíst mapu")).not.toBeInTheDocument();
  });
});
