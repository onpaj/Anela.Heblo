import React from "react";
import { useTranslation } from "react-i18next";
import { ManufactureOrderState } from "../../../api/generated/api-client";
import { stateColors } from "../../../constants/manufactureOrderStates";

interface ManufactureOrderStateChipProps {
  state: ManufactureOrderState;
  size?: "sm" | "md" | "lg";
  className?: string;
}

const ManufactureOrderStateChip: React.FC<ManufactureOrderStateChipProps> = ({
  state,
  size = "md",
  className = "",
}) => {
  const { t } = useTranslation();

  const getStateLabel = (state: ManufactureOrderState): string => {
    return t(`manufacture.states.${ManufactureOrderState[state]}`);
  };

  const sizeClasses = {
    sm: "px-2 py-0.5 text-xs",
    md: "px-2.5 py-0.5 text-xs",
    lg: "px-3 py-1 text-sm",
  };

  return (
    <span
      className={`inline-flex items-center rounded-full font-medium ${sizeClasses[size]} ${stateColors[state]} ${className}`}
    >
      {getStateLabel(state)}
    </span>
  );
};

export default ManufactureOrderStateChip;