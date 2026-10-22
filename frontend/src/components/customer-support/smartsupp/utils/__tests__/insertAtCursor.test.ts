import { insertAtCursor } from "../insertAtCursor";

describe("insertAtCursor", () => {
  it("inserts at the caret and returns the new caret position", () => {
    expect(insertAtCursor("ahoj světe", 5, 5, "😊")).toEqual({ value: "ahoj 😊světe", caret: 7 });
  });

  it("replaces the selected range", () => {
    expect(insertAtCursor("ahoj světe", 5, 10, "🌿")).toEqual({ value: "ahoj 🌿", caret: 7 });
  });
});
