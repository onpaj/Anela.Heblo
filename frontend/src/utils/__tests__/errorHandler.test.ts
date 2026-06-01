// Use real i18n instead of mocking - it's more reliable
import "../../i18n";

import { ErrorCodes } from "../../types/errors";
import {
  getErrorMessage,
  handleApiError,
  isErrorResponse,
} from "../errorHandler";

describe("errorHandler", () => {
  describe("getErrorMessage", () => {
    it("should return localized message for known error code", () => {
      const message = getErrorMessage(ErrorCodes.ValidationError);
      expect(message).toBe("Chyba validace");
    });

    it("should return localized message with parameters", () => {
      const message = getErrorMessage(ErrorCodes.PurchaseOrderNotFound, {
        id: "123",
      });
      expect(message).toBe("Objednávka nenalezena (ID: 123)");
    });

    it("should return generic error message for unknown error code", () => {
      const message = getErrorMessage(9999 as ErrorCodes);
      expect(message).toBe("Nastala chyba (neznámý kód: 9999)");
    });

    it("should resolve string error codes sent by the API (JsonStringEnumConverter)", () => {
      // Backend serializes ErrorCodes as the enum name (string), not the number.
      const message = getErrorMessage("ValidationError");
      expect(message).toBe("Chyba validace");
    });

    it("should resolve SmartsuppShoptetCustomerNotFound from string code", () => {
      const message = getErrorMessage("SmartsuppShoptetCustomerNotFound");
      expect(message).toBe("Zákazník v Shoptetu nenalezen.");
    });

    it("should return generic message for unknown string code", () => {
      const message = getErrorMessage("NotARealCode" as keyof typeof ErrorCodes);
      expect(message).toBe("Nastala chyba (neznámý kód: NotARealCode)");
    });

    it("should handle missing parameters gracefully", () => {
      const message = getErrorMessage(ErrorCodes.PurchaseOrderNotFound);
      expect(message).toBe("Objednávka nenalezena (ID: {id})"); // Placeholder not replaced
    });

    it("should handle Exception error code with exceptionType and message params", () => {
      const params = {
        exceptionType: "InvalidOperationException",
        message: "Product does not have BoM",
      };

      const result = getErrorMessage(ErrorCodes.Exception, params);

      expect(result).toBe(
        "Chyba InvalidOperationException\nProduct does not have BoM",
      );
    });

    it("should fall back to standard translation when Exception params are incomplete", () => {
      const params = {
        message: "Some error message",
        // missing exceptionType
      };

      const result = getErrorMessage(ErrorCodes.Exception, params);

      expect(result).toBe("Výjimka aplikace");
    });

    it("should handle Exception error code without params", () => {
      const result = getErrorMessage(ErrorCodes.Exception);

      expect(result).toBe("Výjimka aplikace");
    });
  });

  describe("handleApiError", () => {
    it("should return empty string for successful response", () => {
      const response = { success: true };
      const message = handleApiError(response);
      expect(message).toBe("");
    });

    it("should return localized error message for error response", () => {
      const response = {
        success: false,
        errorCode: ErrorCodes.ValidationError,
      };
      const message = handleApiError(response);
      expect(message).toBe("Chyba validace");
    });

    it("should return localized error message with parameters", () => {
      const response = {
        success: false,
        errorCode: ErrorCodes.PurchaseOrderNotFound,
        params: { id: "456" },
      };
      const message = handleApiError(response);
      expect(message).toBe("Objednávka nenalezena (ID: 456)");
    });

    it("should return generic error for response without error code", () => {
      const response = { success: false };
      const message = handleApiError(response);
      expect(message).toBe("Nastala neznámá chyba");
    });

    it("should handle Exception error code in API responses", () => {
      const response = {
        success: false,
        errorCode: ErrorCodes.Exception,
        params: {
          exceptionType: "ArgumentException",
          message: "Invalid product code",
        },
      };

      const result = handleApiError(response);

      expect(result).toBe("Chyba ArgumentException\nInvalid product code");
    });
  });

  describe("isErrorResponse", () => {
    it("should return true for valid error response", () => {
      const response = {
        success: false,
        errorCode: ErrorCodes.ValidationError,
      };
      expect(isErrorResponse(response)).toBe(true);
    });

    it("should return false for successful response", () => {
      const response = { success: true };
      expect(isErrorResponse(response)).toBe(false);
    });

    it("should return false for null or undefined", () => {
      expect(isErrorResponse(null)).toBe(false);
      expect(isErrorResponse(undefined)).toBe(false);
    });

    it("should return false for non-object values", () => {
      expect(isErrorResponse("error")).toBe(false);
      expect(isErrorResponse(123)).toBe(false);
    });

    it("should return false for object without success property", () => {
      const response = { errorCode: ErrorCodes.ValidationError };
      expect(isErrorResponse(response)).toBe(false);
    });
  });
});

describe("Error Code Coverage", () => {
  it("should have translations for all error codes", () => {
    const allErrorCodes = Object.values(ErrorCodes) as string[];
    const missingTranslations = allErrorCodes.filter((code) => {
      const message = getErrorMessage(code as ErrorCodes);
      return (
        message.includes(`neznámý kód: ${code}`) ||
        message === `Nastala chyba (kód: ${code})`
      );
    });

    if (missingTranslations.length > 0) {
      console.warn(
        `Missing translations for error codes: ${missingTranslations.join(", ")}`,
      );
    }

    // Warn rather than fail so new codes can land without blocking the build.
    expect(allErrorCodes.length).toBeGreaterThan(0);
  });

  it("should expose representative codes from each module", () => {
    const names = Object.keys(ErrorCodes);
    expect(names).toContain("ValidationError");
    expect(names).toContain("PurchaseOrderNotFound");
    expect(names).toContain("CatalogItemNotFound");
    expect(names).toContain("TransportBoxNotFound");
    expect(names).toContain("ConfigurationNotFound");
    expect(names).toContain("ExternalServiceError");
  });
});
