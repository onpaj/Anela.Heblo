import { parseDocument } from "../mindMapDocument";

test("parseDocument throws on malformed json", () => {
  expect(() => parseDocument("not json")).toThrow();
});
