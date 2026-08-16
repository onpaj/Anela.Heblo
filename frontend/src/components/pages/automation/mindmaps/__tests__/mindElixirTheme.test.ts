import { MIND_MAP_PALETTE } from "../mindMapTheme";
import { MIND_MAP_DARK_THEME, MIND_MAP_LIGHT_THEME, themeFor } from "../mindElixirTheme";

test("both themes expose our branch palette", () => {
  expect(MIND_MAP_LIGHT_THEME.palette).toEqual([...MIND_MAP_PALETTE]);
  expect(MIND_MAP_DARK_THEME.palette).toEqual([...MIND_MAP_PALETTE]);
});

test("themes are tagged so mind-elixir picks matching built-in styling", () => {
  expect(MIND_MAP_LIGHT_THEME.type).toBe("light");
  expect(MIND_MAP_DARK_THEME.type).toBe("dark");
});

test("themes have distinct names so changeTheme() actually re-renders", () => {
  // mind-elixir skips a theme change when the name is unchanged.
  expect(MIND_MAP_LIGHT_THEME.name).not.toBe(MIND_MAP_DARK_THEME.name);
});

test("the light theme keeps the template's warm paper background and ink root", () => {
  expect(MIND_MAP_LIGHT_THEME.cssVar?.["--root-bgcolor"]).toBe("#2B2724");
  expect(MIND_MAP_LIGHT_THEME.cssVar?.["--root-color"]).toBe("#FFFFFF");
});

test("themeFor selects by mode", () => {
  expect(themeFor("light")).toBe(MIND_MAP_LIGHT_THEME);
  expect(themeFor("dark")).toBe(MIND_MAP_DARK_THEME);
});

describe("sub-branch links", () => {
  // Parent card spans x 100..300, vertical centre 120.
  // Child's me-parent box spans x 340..540 on the right, vertical centre 250 —
  // and is inset from the visible card by the 12px node gap on each side.
  //
  // `isFirst` is how mind-elixir tells us which kind of box `pL`/`pW` describe.
  // Its link renderer passes `true` only on the call it makes from linkDiv, where
  // the parent is a top-level branch — whose <me-parent> carries no horizontal
  // padding, so its box IS its card. Every deeper link comes from the renderer's
  // own recursion, which passes no flag at all; those parents sit under
  // <me-children>, where <me-parent> has `padding: 6px var(--node-gap-x)` and the
  // box is therefore a node gap wider than the card on each side. Measured in a
  // browser against the real library: the correlation is exact at every depth.
  const params = {
    pT: 100, pL: 100, pW: 200, pH: 40,
    cT: 220, cL: 340, cW: 200, cH: 60,
    isFirst: true,
  };

  it("both themes draw sub-branches themselves rather than using the library default", () => {
    // The default is a curve into a horizontal line under the child, which clashes
    // with the card every node now has.
    expect(typeof MIND_MAP_LIGHT_THEME.generateSubBranch).toBe("function");
    expect(typeof MIND_MAP_DARK_THEME.generateSubBranch).toBe("function");
  });

  it("attaches to the side centre of both cards, like a top-level branch", () => {
    const path = MIND_MAP_LIGHT_THEME.generateSubBranch!.call(
      null as never,
      { ...params, direction: "rhs" } as never,
    );
    // Leaves the parent's right edge (300) at its vertical centre (120) and lands on
    // the child's LEFT edge pulled in by the node gap (340 + 12 = 352) at centre 250.
    expect(path).toBe("M300,120 C326,120 326,250 352,250");
  });

  it("mirrors the attachment on the left-hand side", () => {
    const path = MIND_MAP_LIGHT_THEME.generateSubBranch!.call(
      null as never,
      { ...params, cL: -240, direction: "lhs" } as never,
    );
    // Parent's LEFT edge (100), child's RIGHT edge pulled in by the gap
    // (-240 + 200 - 12 = -52).
    expect(path).toBe("M100,120 C24,120 24,250 -52,250");
  });

  it("insets the parent end too when the parent is a deeper node, not a branch", () => {
    // Without `isFirst`, pL/pW describe a padded <me-parent> box, so the card's
    // right edge is 300 - 12 = 288. Leaving from the raw box edge (300) started
    // every link a node gap out in open space, detached from the card it belongs to.
    const path = MIND_MAP_LIGHT_THEME.generateSubBranch!.call(
      null as never,
      { ...params, isFirst: undefined, direction: "rhs" } as never,
    );
    expect(path).toBe("M288,120 C320,120 320,250 352,250");
  });

  it("mirrors the deeper-parent inset on the left-hand side", () => {
    // Card's left edge is 100 + 12 = 112.
    const path = MIND_MAP_LIGHT_THEME.generateSubBranch!.call(
      null as never,
      { ...params, isFirst: undefined, cL: -240, direction: "lhs" } as never,
    );
    expect(path).toBe("M112,120 C30,120 30,250 -52,250");
  });

  it("keeps the node gap it insets by in step with the --node-gap-x it publishes", () => {
    // The inset is only correct while these two agree; drifting them apart detaches
    // every sub-branch link from its card.
    const gap = MIND_MAP_LIGHT_THEME.cssVar!["--node-gap-x"];
    const path = MIND_MAP_LIGHT_THEME.generateSubBranch!.call(
      null as never,
      { ...params, direction: "rhs" } as never,
    );
    const childX = Number(path.split(" ").pop()!.split(",")[0]);
    expect(childX - params.cL).toBe(parseInt(gap!, 10));
  });
});
