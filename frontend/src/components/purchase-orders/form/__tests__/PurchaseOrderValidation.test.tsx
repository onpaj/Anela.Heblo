import {
  validateForm,
  clearFieldError,
} from "../PurchaseOrderValidation";
import { FormData } from "../PurchaseOrderTypes";
import {
  PurchaseOrderLineDto,
  SupplierDto,
} from "../../../../api/generated/api-client";
import { MaterialForPurchaseDto } from "../../../../api/hooks/useMaterials";

type FormLine = FormData["lines"][number];

const makeSupplier = (
  overrides: Partial<SupplierDto> = {},
): SupplierDto =>
  Object.assign(new SupplierDto(), {
    id: 1,
    name: "Acme",
    ...overrides,
  });

const makeMaterial = (
  overrides: Partial<MaterialForPurchaseDto> = {},
): MaterialForPurchaseDto =>
  ({
    productCode: "MAT001",
    productName: "Material A",
    ...overrides,
  }) as MaterialForPurchaseDto;

const makeLine = (overrides: Partial<FormLine> = {}): FormLine =>
  Object.assign(new PurchaseOrderLineDto(), {
    quantity: 1,
    unitPrice: 1,
    selectedMaterial: makeMaterial(),
    ...overrides,
  }) as FormLine;

const makeFormData = (overrides: Partial<FormData> = {}): FormData => ({
  orderNumber: "PO-1",
  selectedSupplier: makeSupplier(),
  orderDate: "2026-01-01",
  expectedDeliveryDate: "2026-01-05",
  contactVia: null,
  notes: "",
  lines: [],
  ...overrides,
});

describe("PurchaseOrderValidation", () => {
  it("returns no errors for a baseline-valid form with no lines", () => {
    const errors = validateForm(makeFormData());
    expect(errors).toEqual({});
  });

  describe("validateForm", () => {
    describe("orderNumber", () => {
      it("returns error when orderNumber is empty string", () => {
        const errors = validateForm(makeFormData({ orderNumber: "" }));
        expect(errors.orderNumber).toBe("Číslo objednávky je povinné");
      });

      it("returns error when orderNumber is whitespace-only", () => {
        const errors = validateForm(makeFormData({ orderNumber: "   " }));
        expect(errors.orderNumber).toBe("Číslo objednávky je povinné");
      });

      it("produces no orderNumber error when orderNumber is a non-empty trimmed string", () => {
        const errors = validateForm(makeFormData({ orderNumber: "PO-42" }));
        expect(errors).not.toHaveProperty("orderNumber");
      });
    });

    describe("selectedSupplier", () => {
      it("returns error when selectedSupplier is null", () => {
        const errors = validateForm(makeFormData({ selectedSupplier: null }));
        expect(errors.selectedSupplier).toBe("Dodavatel je povinný");
      });

      it("produces no selectedSupplier error when a supplier is set", () => {
        const errors = validateForm(makeFormData());
        expect(errors).not.toHaveProperty("selectedSupplier");
      });
    });

    describe("orderDate", () => {
      it("returns error when orderDate is empty string", () => {
        const errors = validateForm(
          makeFormData({ orderDate: "", expectedDeliveryDate: "" }),
        );
        expect(errors.orderDate).toBe("Datum objednávky je povinné");
      });

      it("returns error when orderDate is undefined (defensive)", () => {
        const errors = validateForm(
          makeFormData({
            orderDate: undefined as unknown as string,
            expectedDeliveryDate: "",
          }),
        );
        expect(errors.orderDate).toBe("Datum objednávky je povinné");
      });

      it("produces no orderDate error when orderDate is a valid date string", () => {
        const errors = validateForm(makeFormData());
        expect(errors).not.toHaveProperty("orderDate");
      });
    });

    describe("expectedDeliveryDate vs orderDate", () => {
      it("returns error when expectedDeliveryDate is before orderDate", () => {
        const errors = validateForm(
          makeFormData({
            orderDate: "2026-01-10",
            expectedDeliveryDate: "2026-01-05",
          }),
        );
        expect(errors.expectedDeliveryDate).toBe(
          "Datum dodání nemůže být před datem objednávky",
        );
      });

      it("produces no expectedDeliveryDate error when delivery is after order", () => {
        const errors = validateForm(
          makeFormData({
            orderDate: "2026-01-01",
            expectedDeliveryDate: "2026-01-05",
          }),
        );
        expect(errors).not.toHaveProperty("expectedDeliveryDate");
      });

      it("produces no expectedDeliveryDate error when delivery equals order (boundary)", () => {
        const sameDay = "2026-01-01";
        const errors = validateForm(
          makeFormData({
            orderDate: sameDay,
            expectedDeliveryDate: sameDay,
          }),
        );
        expect(errors).not.toHaveProperty("expectedDeliveryDate");
      });

      it("produces no expectedDeliveryDate error when expectedDeliveryDate is empty", () => {
        const errors = validateForm(
          makeFormData({
            orderDate: "2026-01-01",
            expectedDeliveryDate: "",
          }),
        );
        expect(errors).not.toHaveProperty("expectedDeliveryDate");
      });
    });

    describe("per-line validation", () => {
      it("produces no line errors when selectedMaterial is null (silent skip)", () => {
        const line = makeLine({
          selectedMaterial: null,
          quantity: 0,
          unitPrice: 0,
        });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors).not.toHaveProperty("line_0_material");
        expect(errors).not.toHaveProperty("line_0_quantity");
        expect(errors).not.toHaveProperty("line_0_price");
      });

      it("produces no line errors when selectedMaterial.productName is empty string (silent skip)", () => {
        const line = makeLine({
          selectedMaterial: makeMaterial({ productName: "" }),
          quantity: -5,
          unitPrice: -1,
        });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors).not.toHaveProperty("line_0_material");
        expect(errors).not.toHaveProperty("line_0_quantity");
        expect(errors).not.toHaveProperty("line_0_price");
      });

      it("produces no line errors when selectedMaterial.productName is undefined (silent skip)", () => {
        const line = makeLine({
          selectedMaterial: makeMaterial({ productName: undefined }),
          quantity: 0,
          unitPrice: 0,
        });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors).not.toHaveProperty("line_0_material");
        expect(errors).not.toHaveProperty("line_0_quantity");
        expect(errors).not.toHaveProperty("line_0_price");
      });

      it("returns material error when productName is whitespace-only (gate passes via truthy check, inner trim fails)", () => {
        const line = makeLine({
          selectedMaterial: makeMaterial({ productName: "   " }),
          quantity: 1,
          unitPrice: 1,
        });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_material).toBe("Vyberte materiál ze seznamu");
      });

      it("returns quantity error when quantity is 0", () => {
        const line = makeLine({ quantity: 0 });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_quantity).toBe("Množství musí být větší než 0");
      });

      it("returns quantity error when quantity is negative", () => {
        const line = makeLine({ quantity: -1 });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_quantity).toBe("Množství musí být větší než 0");
      });

      it("returns quantity error when quantity is undefined", () => {
        const line = makeLine({ quantity: undefined });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_quantity).toBe("Množství musí být větší než 0");
      });

      it("returns unit price error when unitPrice is 0", () => {
        const line = makeLine({ unitPrice: 0 });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_price).toBe("Jednotková cena musí být větší než 0");
      });

      it("returns unit price error when unitPrice is negative", () => {
        const line = makeLine({ unitPrice: -0.01 });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_price).toBe("Jednotková cena musí být větší než 0");
      });

      it("returns unit price error when unitPrice is undefined", () => {
        const line = makeLine({ unitPrice: undefined });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors.line_0_price).toBe("Jednotková cena musí být větší než 0");
      });

      it("produces no line errors when material, quantity, and unitPrice are all valid", () => {
        const line = makeLine({ quantity: 2, unitPrice: 9.99 });
        const errors = validateForm(makeFormData({ lines: [line] }));
        expect(errors).not.toHaveProperty("line_0_material");
        expect(errors).not.toHaveProperty("line_0_quantity");
        expect(errors).not.toHaveProperty("line_0_price");
      });

      it("uses the line index in error keys for the second line", () => {
        const validLine = makeLine({ quantity: 1, unitPrice: 1 });
        const invalidLine = makeLine({ quantity: 0, unitPrice: 0 });
        const errors = validateForm(
          makeFormData({ lines: [validLine, invalidLine] }),
        );
        expect(errors).not.toHaveProperty("line_0_quantity");
        expect(errors).not.toHaveProperty("line_0_price");
        expect(errors.line_1_quantity).toBe("Množství musí být větší než 0");
        expect(errors.line_1_price).toBe("Jednotková cena musí být větší než 0");
      });
    });
  });

  describe("clearFieldError", () => {
    it("returns a new object without the field when the field has a truthy value", () => {
      const errors = { foo: "msg-foo", bar: "msg-bar" };
      const result = clearFieldError(errors, "foo");
      expect(result).not.toBe(errors);
      expect(result).toEqual({ bar: "msg-bar" });
    });

    it("does not mutate the original errors object when removing a field", () => {
      const errors = { foo: "msg-foo", bar: "msg-bar" };
      clearFieldError(errors, "foo");
      expect(errors).toEqual({ foo: "msg-foo", bar: "msg-bar" });
    });

    it("returns the same reference when the field is not present", () => {
      const errors = { foo: "msg-foo" };
      const result = clearFieldError(errors, "missing");
      expect(result).toBe(errors);
    });

    it("returns the same reference when the field has an empty-string (falsy) value", () => {
      const errors = { foo: "" };
      const result = clearFieldError(errors, "foo");
      expect(result).toBe(errors);
    });

    it("does not mutate the original errors object when the field is absent", () => {
      const errors = { foo: "msg-foo" };
      clearFieldError(errors, "missing");
      expect(errors).toEqual({ foo: "msg-foo" });
    });
  });
});
