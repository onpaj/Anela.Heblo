import React from "react";
import { render } from "@testing-library/react";
import * as ReactDOM from "react-dom";
import ToastContainer from "../ToastContainer";

jest.spyOn(ReactDOM, "createPortal").mockImplementation(function (node) {
  return node;
});

describe("ToastContainer", () => {
  it("uses responsive classes - full-bleed on mobile, right-anchored on sm+", () => {
    const toast = {
      id: "1",
      type: "info",
      title: "Test toast",
    };

    render(
      <div data-testid="portal-root">
        <ToastContainer toasts={[toast]} onClose={jest.fn()} />
      </div>,
    );

    // eslint-disable-next-line testing-library/no-node-access
    const wrapper = document.querySelector("div.fixed") as HTMLElement;
    expect(wrapper).not.toBeNull();
    const classes = wrapper.className.split(/\s+/);
    expect(classes).toContain("left-4");
    expect(classes).toContain("sm:left-auto");
    expect(classes).toContain("sm:min-w-[600px]");
    expect(classes).not.toContain("min-w-[600px]");
  });
});
