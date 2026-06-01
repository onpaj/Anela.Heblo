import React from "react";
import { FileText, Calendar, Truck, Phone } from "lucide-react";
import { ContactVia } from "../../../api/generated/api-client";
import SupplierAutocomplete from "../../common/SupplierAutocomplete";
import { PurchaseOrderHeaderProps } from "./PurchaseOrderTypes";

const PurchaseOrderHeader: React.FC<PurchaseOrderHeaderProps> = ({
  formData,
  errors,
  onInputChange,
  onSupplierSelect,
}) => {
  return (
    <div className="space-y-3">
      <h3 className="text-lg font-medium text-gray-900 flex items-center">
        <FileText className="h-5 w-5 mr-2 text-gray-500" />
        Základní informace
      </h3>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Left side - Basic info */}
        <div className="space-y-3">
          {/* Order Number */}
          <div>
            <label
              htmlFor="orderNumber"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              <FileText className="h-4 w-4 inline mr-1" />
              Číslo objednávky *
            </label>
            <input
              type="text"
              id="orderNumber"
              value={formData.orderNumber}
              onChange={(e) => onInputChange("orderNumber", e.target.value)}
              className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                errors.orderNumber ? "border-red-300" : "border-gray-300"
              }`}
              placeholder="Číslo objednávky (např. PO20250101-1015)"
            />
            {errors.orderNumber && (
              <p className="mt-1 text-sm text-red-600">{errors.orderNumber}</p>
            )}
          </div>

          {/* Supplier Autocomplete */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              <Truck className="h-4 w-4 inline mr-1" />
              Dodavatel *
            </label>
            <SupplierAutocomplete
              value={formData.selectedSupplier}
              onSelect={onSupplierSelect}
              placeholder="Vyberte dodavatele..."
              error={errors.selectedSupplier}
              className="w-full"
            />
          </div>

          {/* Dates and Contact in row */}
          <div className="grid grid-cols-3 gap-3">
            {/* Order Date */}
            <div>
              <label
                htmlFor="orderDate"
                className="block text-sm font-medium text-gray-700 mb-1"
              >
                <Calendar className="h-4 w-4 inline mr-1" />
                Objednáno *
              </label>
              <input
                type="date"
                id="orderDate"
                value={formData.orderDate}
                onChange={(e) => onInputChange("orderDate", e.target.value)}
                className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                  errors.orderDate ? "border-red-300" : "border-gray-300"
                }`}
              />
              {errors.orderDate && (
                <p className="mt-1 text-sm text-red-600">{errors.orderDate}</p>
              )}
            </div>

            {/* Expected Delivery Date */}
            <div>
              <label
                htmlFor="expectedDeliveryDate"
                className="block text-sm font-medium text-gray-700 mb-1"
              >
                <Calendar className="h-4 w-4 inline mr-1" />
                Dodání
              </label>
              <input
                type="date"
                id="expectedDeliveryDate"
                value={formData.expectedDeliveryDate}
                onChange={(e) =>
                  onInputChange("expectedDeliveryDate", e.target.value)
                }
                className={`block w-full px-3 py-1.5 text-sm border rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 ${
                  errors.expectedDeliveryDate
                    ? "border-red-300"
                    : "border-gray-300"
                }`}
              />
              {errors.expectedDeliveryDate && (
                <p className="mt-1 text-sm text-red-600">
                  {errors.expectedDeliveryDate}
                </p>
              )}
            </div>

            {/* Contact Via */}
            <div>
              <label
                htmlFor="contactVia"
                className="block text-sm font-medium text-gray-700 mb-1"
              >
                <Phone className="h-4 w-4 inline mr-1" />
                Kontakt
              </label>
              <select
                id="contactVia"
                value={formData.contactVia || ""}
                onChange={(e) =>
                  onInputChange(
                    "contactVia",
                    e.target.value === ""
                      ? null
                      : (e.target.value as ContactVia),
                  )
                }
                className="block w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500"
              >
                <option value="">Vyberte způsob kontaktu</option>
                <option value={ContactVia.Email}>Email</option>
                <option value={ContactVia.Phone}>Telefon</option>
                <option value={ContactVia.WhatsApp}>WhatsApp</option>
                <option value={ContactVia.F2F}>Osobne</option>
                <option value={ContactVia.Eshop}>Eshop</option>
                <option value={ContactVia.Other}>Jine</option>
              </select>
            </div>
          </div>
        </div>

        {/* Right side - Notes section */}
        <div className="space-y-3">
          <h4 className="text-md font-medium text-gray-900 flex items-center">
            <FileText className="h-4 w-4 mr-2 text-gray-500" />
            Poznámky
          </h4>

          <div>
            <label
              htmlFor="supplierNote"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Poznámka od dodavatele
            </label>
            <div className="bg-gray-50 rounded-md p-3 min-h-[60px] text-sm text-gray-600 italic border">
              Poznámka od dodavatele se zobrazí po vytvoření objednávky
            </div>
          </div>

          <div>
            <label
              htmlFor="notes"
              className="block text-sm font-medium text-gray-700 mb-1"
            >
              Poznámky k objednávce
            </label>
            <textarea
              id="notes"
              value={formData.notes}
              onChange={(e) => onInputChange("notes", e.target.value)}
              rows={4}
              className="block w-full px-3 py-2 text-sm border border-gray-300 rounded-md shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 resize-none"
              placeholder="Volitelné poznámky k objednávce..."
            />
          </div>
        </div>
      </div>
    </div>
  );
};

export default PurchaseOrderHeader;
