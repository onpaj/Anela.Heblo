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
