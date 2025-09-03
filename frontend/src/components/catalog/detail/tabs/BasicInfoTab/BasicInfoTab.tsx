import React from 'react';
import { CatalogItemDto } from '../../../../../api/hooks/useCatalog';
import ProductBasicInfo from './ProductBasicInfo';
import ProductStockInfo from './ProductStockInfo';
import ProductPriceInfo from './ProductPriceInfo';
import ProductPropertiesInfo from './ProductPropertiesInfo';

interface BasicInfoTabProps {
  item: CatalogItemDto;
  onManufactureDifficultyClick: () => void;
}

const BasicInfoTab: React.FC<BasicInfoTabProps> = ({ item, onManufactureDifficultyClick }) => {
  return (
    <div className="space-y-6">
      <ProductBasicInfo item={item} />
      <ProductStockInfo item={item} />
      <ProductPriceInfo item={item} />
      <ProductPropertiesInfo 
        item={item} 
        onManufactureDifficultyClick={onManufactureDifficultyClick} 
      />
    </div>
  );
};

export default BasicInfoTab;