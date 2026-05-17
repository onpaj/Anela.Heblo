import React from "react";
import { render } from "@testing-library/react";
import * as ReactDOM from "react-dom";
import ToastContainer from "../ToastContainer";

jest.spyOn(ReactDOM, "createPortal").mockImplementation(function (node) {
  return node;
});

describe("ToastContainer", () => {
  it("uses responsive classes — full-bleed on mobile, right-anchored on sm+", () => {
    const toast = {
      id: "1",
      type: "info",
      title: "Test toast",
      onClose: jest.fn(),
    };

    render(
      <div data-testid="portal-root">
        <ToastContainer toasts={[toast]} onClose={jest.fn()} />
      </div>,
    );

    const wrapper = document.querySelector("div.fixed");
    expect(wrapper).toBeTruthy();
    expect(wrapper.className).toContain("left-4");
    expect(wrapper.className).toContain("sm:left-auto");
    expect(wrapper.className).toContain("sm:min-w-[600px]");
    expect(wrapper.className).not.toContain("min-w-[600px]");
  });
});
