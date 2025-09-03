import React from 'react';
import { Layers, Settings } from 'lucide-react';
import { CatalogItemDto } from '../../../../../api/hooks/useCatalog';

interface ProductPropertiesInfoProps {
  item: CatalogItemDto;
  onManufactureDifficultyClick: () => void;
}

const ProductPropertiesInfo: React.FC<ProductPropertiesInfoProps> = ({ 
  item, 
  onManufactureDifficultyClick 
}) => {
  return (
    <div className="space-y-4">
      <h3 className="text-lg font-medium text-gray-900 flex items-center">
        <Layers className="h-5 w-5 mr-2 text-gray-500" />
        Vlastnosti produktu
      </h3>
      
      <div className="bg-gray-50 rounded-lg p-4">
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Optimální zásoby (dny)
            </span>
            <span className="text-lg font-semibold text-gray-900">
              {item.properties?.optimalStockDaysSetup || '-'}
            </span>
          </div>
          
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Min. zásoba
            </span>
            <span className="text-lg font-semibold text-gray-900">
              {item.properties?.stockMinSetup || '-'}
            </span>
          </div>
          
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Velikost šarže
            </span>
            <span className="text-lg font-semibold text-gray-900">
              {item.properties?.batchSize || '-'}
            </span>
          </div>
          
          <div className="text-center">
            <span className="text-xs font-medium text-gray-600 block mb-1">
              Náročnost výroby
            </span>
            <button
              onClick={onManufactureDifficultyClick}
              className="text-lg font-semibold text-indigo-600 hover:text-indigo-700 hover:underline focus:outline-none focus:underline flex items-center space-x-1 mx-auto"
              title="Klikněte pro správu náročnosti výroby"
            >
              <span>
                {item.manufactureDifficulty && item.manufactureDifficulty > 0 
                  ? item.manufactureDifficulty.toFixed(2)
                  : 'Nenastaveno'
                }
              </span>
              <Settings className="h-3 w-3" />
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

export default ProductPropertiesInfo;