import React from "react";
import { PackageOpen, AlertTriangle, CheckCircle } from "lucide-react";
import { GiftPackage } from "./GiftPackageManufacturingList";

interface DisassemblyTabContentProps {
  selectedPackage: GiftPackage;
  quantity: number;
  setQuantity: (quantity: number) => void;
  maxQuantity: number;
  onDisassemble: () => Promise<void>;
  isPending: boolean;
}

const DisassemblyTabContent: React.FC<DisassemblyTabContentProps> = ({
  selectedPackage,
  quantity,
  setQuantity,
  maxQuantity,
  onDisassemble,
  isPending,
}) => {
  const isValid = quantity > 0 && quantity <= maxQuantity;

  return (
    <div className="flex-1 p-3 sm:p-4 bg-red-50/30">
      {/* Warning Banner */}
      <div className="mb-4 p-3 bg-red-100 border-2 border-red-200 rounded-lg">
        <div className="flex items-start">
          <AlertTriangle className="h-5 w-5 text-red-600 mr-2 flex-shrink-0 mt-0.5" />
          <div>
            <div className="text-sm font-semibold text-red-800">
              Pozor: Destruktivní operace
            </div>
            <div className="text-xs text-red-700 mt-1">
              Rozebráním balíčku vrátíte komponenty zpět na sklad a odeberete hotový výrobek.
            </div>
          </div>
        </div>
      </div>

      {/* Statistics Card */}
      <div className="mb-4 p-3 bg-white border-2 border-red-200 rounded-lg">
        <div className="text-sm font-medium text-gray-700 mb-2">Dostupné k rozebírání</div>
        <div className="text-2xl font-bold text-gray-900">{maxQuantity} ks</div>
        <div className="text-xs text-gray-500 mt-1">Aktuální sklad hotových balíčků</div>
      </div>

      <h3 className="text-sm sm:text-base font-medium text-gray-900 mb-4 flex items-center">
        <PackageOpen className="h-4 w-4 mr-2 text-red-600" />
        Množství k rozebírání
      </h3>

      {/* Quantity Input - Touch Friendly with Red Theme */}
      <div className="mb-4">
        <div className="flex items-center space-x-2 mb-3">
          <button
            onClick={() => setQuantity(Math.max(1, quantity - 1))}
            className="w-16 h-16 flex items-center justify-center bg-white border-2 border-red-300 rounded-xl text-red-700 hover:bg-red-50 hover:text-red-900 hover:border-red-400 active:bg-red-100 touch-manipulation text-3xl font-bold transition-all duration-150 shadow-sm"
            type="button"
            disabled={isPending}
          >
            -
          </button>
          <input
            type="number"
            min="1"
            max={maxQuantity}
            value={quantity}
            onChange={(e) => {
              const val = parseInt(e.target.value) || 1;
              setQuantity(Math.min(maxQuantity, Math.max(1, val)));
            }}
            className="flex-1 text-center border-2 border-red-300 rounded-xl px-3 py-4 text-xl font-bold focus:outline-none focus:ring-2 focus:ring-red-500 focus:border-red-500 touch-manipulation shadow-sm min-w-0"
            disabled={isPending}
          />
          <button
            onClick={() => setQuantity(Math.min(maxQuantity, quantity + 1))}
            className="w-16 h-16 flex items-center justify-center bg-white border-2 border-red-300 rounded-xl text-red-700 hover:bg-red-50 hover:text-red-900 hover:border-red-400 active:bg-red-100 touch-manipulation text-3xl font-bold transition-all duration-150 shadow-sm"
            type="button"
            disabled={isPending}
          >
            +
          </button>
        </div>

        {/* Quick buttons */}
        <div className="grid grid-cols-2 gap-2 mb-4">
          <button
            onClick={() => setQuantity(Math.max(1, Math.floor(maxQuantity / 2)))}
            className="px-3 py-2 text-sm bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors touch-manipulation text-center"
            type="button"
            disabled={isPending || maxQuantity < 1}
          >
            Půlka<br />({Math.max(1, Math.floor(maxQuantity / 2))})
          </button>
          <button
            onClick={() => setQuantity(maxQuantity)}
            className="px-3 py-2 text-sm bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors touch-manipulation text-center"
            type="button"
            disabled={isPending || maxQuantity < 1}
          >
            Vše<br />({maxQuantity})
          </button>
        </div>
      </div>

      {/* Validation Status */}
      <div
        className={`p-3 rounded-lg mb-4 ${
          isValid
            ? "bg-green-100 border border-green-200"
            : "bg-red-100 border border-red-200"
        }`}
      >
        <div className="flex items-center">
          {isValid ? (
            <>
              <CheckCircle className="h-4 w-4 text-green-600 mr-2" />
              <span className="text-sm font-medium text-green-800">
                Množství je v pořádku
              </span>
            </>
          ) : (
            <>
              <AlertTriangle className="h-4 w-4 text-red-600 mr-2" />
              <span className="text-sm font-medium text-red-800">
                {quantity > maxQuantity
                  ? `Překročen maximální dostupný počet (max: ${maxQuantity})`
                  : "Množství musí být větší než 0"}
              </span>
            </>
          )}
        </div>
      </div>

      {/* Disassembly Button - Touch Friendly with Red Theme */}
      <button
        onClick={onDisassemble}
        disabled={!isValid || isPending}
        className={`w-full flex items-center justify-center px-6 py-4 text-lg font-semibold rounded-lg transition-colors touch-manipulation ${
          isValid && !isPending
            ? "text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
            : "text-gray-400 bg-gray-200 cursor-not-allowed"
        }`}
      >
        {isPending ? (
          <>
            <div className="animate-spin rounded-full h-5 w-5 border-b-2 border-white mr-2" />
            Rozebírám...
          </>
        ) : (
          <>
            <PackageOpen className="h-5 w-5 mr-2" />
            Rozebrat balíček ({quantity} ks)
          </>
        )}
      </button>
    </div>
  );
};

export default DisassemblyTabContent;
