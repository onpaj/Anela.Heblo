import React, { useState, useEffect } from "react";
import { BrowserRouter as Router, Routes, Route } from "react-router-dom";
import { MsalProvider } from "@azure/msal-react";
import { PublicClientApplication } from "@azure/msal-browser";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import Layout from "./components/Layout/Layout";
import Dashboard from "./components/pages/Dashboard";
import CatalogList from "./components/pages/CatalogList";
import PurchaseOrderList from "./components/pages/PurchaseOrderList";
import PurchaseStockAnalysis from "./components/pages/PurchaseStockAnalysis";
import ManufacturingStockAnalysis from "./components/pages/ManufacturingStockAnalysis";
import ManufactureOutput from "./components/pages/ManufactureOutput";
import ManufactureBatchCalculator from "./components/pages/ManufactureBatchCalculator";
import ProductMarginsList from "./components/pages/ProductMarginsList";
import ProductMarginSummary from "./components/pages/ProductMarginSummary";
import FinancialOverview from "./components/pages/FinancialOverview";
import JournalList from "./components/pages/Journal/JournalList";
import JournalEntryNew from "./components/pages/JournalEntryNew";
import JournalEntryEdit from "./components/pages/JournalEntryEdit";
import TransportBoxList from "./components/pages/TransportBoxList";
import AuthGuard from "./components/auth/AuthGuard";
import { StatusBar } from "./components/StatusBar";
import { loadConfig, Config } from "./config/runtimeConfig";
import { setGlobalTokenProvider, setGlobalToastHandler } from "./api/client";
import { apiRequest } from "./auth/msalConfig";
import { isE2ETestMode, getE2EAccessToken } from "./auth/e2eAuth";
import { ToastProvider } from "./contexts/ToastContext";
import { LoadingProvider } from "./contexts/LoadingContext";
import { GlobalLoadingIndicator } from "./components/GlobalLoadingIndicator";
import { AppInitializer } from "./components/AppInitializer";
import "./i18n";

// Create a client
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000, // 5 minutes
      gcTime: 10 * 60 * 1000, // 10 minutes
      retry: 1,
    },
  },
});

function App() {
  const [config, setConfig] = useState<Config | null>(null);
  const [msalInstance, setMsalInstance] =
    useState<PublicClientApplication | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        console.log("🚀 Initializing Anela Heblo application...");

        // Load configuration from environment variables
        const appConfig = loadConfig();
        setConfig(appConfig);

        // Create MSAL configuration with app configuration
        const msalConfig = {
          auth: {
            clientId: appConfig.azureClientId || "mock-client-id",
            authority:
              appConfig.azureAuthority ||
              "https://login.microsoftonline.com/mock-tenant-id",
            redirectUri: window.location.origin,
            postLogoutRedirectUri: window.location.origin,
            clientCapabilities: ["CP1"],
          },
          cache: {
            cacheLocation: "sessionStorage" as const,
            storeAuthStateInCookie: false,
          },
          system: {
            allowNativeBroker: false,
          },
        };

        // Create MSAL instance with app configuration
        console.log("📱 Creating MSAL instance...");
        const instance = new PublicClientApplication(msalConfig);
        setMsalInstance(instance);

        // Set global token provider for API client
        if (isE2ETestMode()) {
          console.log("🧪 Setting up E2E test authentication token provider");
          setGlobalTokenProvider(async () => {
            console.log("🎫 Providing E2E test token");
            return getE2EAccessToken();
          });
        } else if (!appConfig.useMockAuth) {
          console.log("🔐 Setting up real authentication token provider");
          setGlobalTokenProvider(async () => {
            try {
              const accounts = instance.getAllAccounts();
              if (accounts.length === 0) {
                console.warn("No accounts found in MSAL instance");
                return null;
              }

              const account = accounts[0];
              const response = await instance.acquireTokenSilent({
                ...apiRequest,
                account: account,
              });

              console.log("✅ Token acquired successfully");
              return response.accessToken;
            } catch (error) {
              console.error(
                "❌ Failed to acquire token in global provider:",
                error,
              );
              return null;
            }
          });
        } else {
          console.log(
            "🧪 Using mock authentication - no token provider needed",
          );
        }

        console.log("✅ Application initialized successfully");
      } catch (err) {
        console.error("❌ Critical error during app initialization:", err);

        // Provide more specific error messages
        if (err instanceof Error) {
          if (err.message.includes("ClientConfigurationError")) {
            setError(
              "Invalid Azure AD configuration. Check REACT_APP_AZURE_CLIENT_ID and REACT_APP_AZURE_AUTHORITY environment variables.",
            );
          } else if (err.message.includes("network")) {
            setError(
              "Network error during initialization. Check API connectivity.",
            );
          } else {
            setError(`Initialization failed: ${err.message}`);
          }
        } else {
          setError("Unknown error during application initialization");
        }

        console.error(
          "🔄 Consider checking environment variables or falling back to mock mode",
        );
      } finally {
        setLoading(false);
      }
    };

    initializeApp();
  }, []);

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="text-center">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600 mx-auto mb-4"></div>
          <p className="text-gray-600">Initializing application...</p>
        </div>
      </div>
    );
  }

  if (error || !config || !msalInstance) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-gray-50">
        <div className="max-w-md w-full mx-4">
          <div className="bg-white rounded-lg shadow-lg p-8 text-center">
            <div className="text-red-600 mb-4">
              <svg
                className="w-12 h-12 mx-auto mb-2"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  strokeWidth={2}
                  d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                />
              </svg>
            </div>

            <h1 className="text-xl font-semibold text-gray-900 mb-4">
              Application Initialization Failed
            </h1>

            <p className="text-gray-600 mb-6">
              {error || "Failed to initialize application"}
            </p>

            <div className="bg-yellow-50 border border-yellow-200 rounded-lg p-4 text-left">
              <h3 className="text-sm font-medium text-yellow-800 mb-2">
                Troubleshooting Tips:
              </h3>
              <ul className="text-xs text-yellow-700 space-y-1">
                <li>• Check browser console for detailed error logs</li>
                <li>• Verify environment variables are set correctly</li>
                <li>
                  • For local development, set REACT_APP_USE_MOCK_AUTH=true
                </li>
                <li>• Ensure API backend is running and accessible</li>
              </ul>
            </div>

            <button
              onClick={() => window.location.reload()}
              className="mt-6 px-4 py-2 bg-indigo-600 text-white rounded-md hover:bg-indigo-700 transition-colors"
            >
              Retry
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <QueryClientProvider client={queryClient}>
      <LoadingProvider>
        <ToastProvider>
          <AppInitializer>
            <div className="App min-h-screen" data-testid="app">
              <MsalProvider instance={msalInstance}>
                <Router>
                  <AuthGuard>
                    <Layout statusBar={<StatusBar />}>
                      <Routes>
                        <Route path="/" element={<Dashboard />} />
                        <Route
                          path="/finance/overview"
                          element={<FinancialOverview />}
                        />
                        <Route
                          path="/analytics/product-margin-summary"
                          element={<ProductMarginSummary />}
                        />
                        <Route path="/catalog" element={<CatalogList />} />
                        <Route
                          path="/purchase/orders"
                          element={<PurchaseOrderList />}
                        />
                        <Route
                          path="/purchase/stock-analysis"
                          element={<PurchaseStockAnalysis />}
                        />
                        <Route
                          path="/manufacturing/stock-analysis"
                          element={<ManufacturingStockAnalysis />}
                        />
                        <Route
                          path="/manufacturing/output"
                          element={<ManufactureOutput />}
                        />
                        <Route
                          path="/manufacturing/batch-calculator"
                          element={<ManufactureBatchCalculator />}
                        />
                        <Route
                          path="/products/margins"
                          element={<ProductMarginsList />}
                        />
                        <Route path="/journal" element={<JournalList />} />
                        <Route
                          path="/journal/new"
                          element={<JournalEntryNew />}
                        />
                        <Route
                          path="/journal/:id/edit"
                          element={<JournalEntryEdit />}
                        />
                        <Route
                          path="/logistics/transport-boxes"
                          element={<TransportBoxList />}
                        />
                      </Routes>
                    </Layout>
                  </AuthGuard>
                </Router>
              </MsalProvider>
              <GlobalLoadingIndicator />
            </div>
          </AppInitializer>
        </ToastProvider>
      </LoadingProvider>
    </QueryClientProvider>
  );
}

export default App;
