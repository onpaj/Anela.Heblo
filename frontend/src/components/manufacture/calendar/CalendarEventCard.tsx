import React from "react";
import { Factory, User } from "lucide-react";
import { CalendarEventDto } from "../../../api/hooks/useManufactureOrders";
import { stateColors, stateBorderColors } from "../../../constants/manufactureOrderStates";
import ManufactureOrderStateChip from "../shared/ManufactureOrderStateChip";

interface CalendarEventCardProps {
  event: CalendarEventDto;
  variant?: "compact" | "full";
  onClick?: () => void;
  className?: string;
}

const CalendarEventCard: React.FC<CalendarEventCardProps> = ({
  event,
  variant = "compact",
  onClick,
  className = "",
}) => {
  const handleClick = () => {
    if (onClick) {
      onClick();
    }
  };

  const getTitle = () => {
    const title = `${event.title || event.orderNumber}\nZakázka: ${event.orderNumber}`;
    const state = event.state ? `\nStav: ${event.state}` : '';
    const responsible = event.responsiblePerson ? `\nOdpovědná osoba: ${event.responsiblePerson}` : '';
    const actionRequired = event.manualActionRequired ? `\n⚠️ Vyžaduje ruční zásah` : '';
    
    return `${title}${state}${responsible}${actionRequired}`;
  };

  if (variant === "compact") {
    return (
      <div
        onClick={handleClick}
        className={`
          text-xs p-1 rounded cursor-pointer transition-all
          ${event.state ? stateColors[event.state] : 'bg-gray-100 text-gray-800'}
          hover:shadow-sm hover:scale-105
          ${className}
        `}
        title={getTitle()}
      >
        <div className="flex items-center space-x-1">
          <Factory className="h-3 w-3 flex-shrink-0" />
          {event.manualActionRequired && (
            <div 
              className="w-2 h-2 bg-red-500 rounded-full flex-shrink-0" 
              title="Vyžaduje ruční zásah"
            />
          )}
          <span className="truncate font-medium">
            {event.title || event.orderNumber}
          </span>
        </div>
        {event.responsiblePerson && (
          <div className="flex items-center space-x-1 text-xs opacity-75">
            <User className="h-2 w-2" />
            <span className="truncate">
              {event.responsiblePerson}
            </span>
          </div>
        )}
      </div>
    );
  }

  return (
    <div
      onClick={handleClick}
      className={`
        p-3 rounded-lg cursor-pointer transition-all border min-h-[200px] flex flex-col
        ${event.state ? `${stateColors[event.state]} ${stateBorderColors[event.state]}` : 'bg-gray-100 text-gray-800 border-gray-200'}
        hover:shadow-md hover:scale-[1.02] transform
        ${className}
      `}
      title={`Klikněte pro detail zakázky ${event.orderNumber}${event.manualActionRequired ? `\n⚠️ Vyžaduje ruční zásah` : ''}`}
    >
      {/* Order Header */}
      <div className="flex items-start justify-between mb-2">
        <div className="flex items-center space-x-2">
          <Factory className="h-4 w-4 text-current flex-shrink-0" />
          <div>
            <div className="font-bold text-sm leading-tight">
              {event.title || event.orderNumber}
            </div>
            <div className="text-xs opacity-75">
              #{event.orderNumber}
            </div>
          </div>
        </div>
        {event.manualActionRequired && (
          <div 
            className="w-2 h-2 bg-red-500 rounded-full flex-shrink-0" 
            title="Vyžaduje ruční zásah"
          />
        )}
      </div>

      {/* State Badge */}
      {event.state !== undefined && (
        <div className="mb-2">
          <ManufactureOrderStateChip state={event.state} size="sm" />
        </div>
      )}

      {/* Responsible Person */}
      {event.responsiblePerson && (
        <div className="flex items-center space-x-1 text-xs opacity-75 mt-auto">
          <User className="h-3 w-3" />
          <span className="truncate">
            {event.responsiblePerson}
          </span>
        </div>
      )}
    </div>
  );
};

export default CalendarEventCard;