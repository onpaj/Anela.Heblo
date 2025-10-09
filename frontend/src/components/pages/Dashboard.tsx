import React from "react";
import {
  useLiveHealthCheck,
  useReadyHealthCheck,
} from "../../api/hooks/useHealth";
import { PAGE_CONTAINER_HEIGHT } from "../../constants/layout";

const Dashboard: React.FC = () => {
  useLiveHealthCheck();
  useReadyHealthCheck();


  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      {/* Header - Fixed */}
      <div className="flex-shrink-0 mb-3 px-4 sm:px-6 lg:px-8">
        <h1 className="text-3xl font-bold text-gray-900">
          Dashboard
        </h1>
        <p className="mt-2 text-gray-600">
          Přehled systému a stavu aplikace
        </p>
      </div>

      {/* Content Area */}
      <div className="flex-1 px-4 sm:px-6 lg:px-8 overflow-auto">
        <div className="bg-white shadow overflow-hidden sm:rounded-md">
          <div className="px-4 py-5 sm:px-6">
            <h3 className="text-lg leading-6 font-medium text-gray-900">
              Systém běží
            </h3>
            <p className="mt-1 max-w-2xl text-sm text-gray-500">
              Background procesy automaticky zpracovávají data
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
