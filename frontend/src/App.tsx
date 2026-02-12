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
import BatchPlanningCalculator from "./components/pages/ManufactureBatchPlanning";
import ProductMarginsList from "./components/pages/ProductMarginsList";
import ProductMarginSummary from "./components/pages/ProductMarginSummary";
import FinancialOverview from "./components/pages/FinancialOverview";
import BankStatementImportChart from "./components/pages/BankStatementImportChart";
import JournalList from "./components/pages/Journal/JournalList";
import JournalEntryNew from "./components/pages/JournalEntryNew";
import JournalEntryEdit from "./components/pages/JournalEntryEdit";
import TransportBoxList from "./components/pages/TransportBoxList";
import TransportBoxReceive from "./components/pages/TransportBoxReceive";
import GiftPackageManufacturing from "./components/pages/GiftPackageManufacturing";
import WarehouseStatistics from "./components/pages/WarehouseStatistics";
import InventoryList from "./components/pages/InventoryList";
import ManufactureInventoryList from "./components/pages/ManufactureInventoryList";
import ManufactureOrderList from "./components/manufacture/pages/ManufactureOrderList";
import ManufactureOrderDetail from "./components/manufacture/pages/ManufactureOrderDetail";
import InvoiceImportStatistics from "./components/pages/automation/InvoiceImportStatistics";
import BackgroundTasks from "./components/pages/automation/BackgroundTasks";
import OrgChartPage from "./pages/OrgChartPage";
import InvoiceClassificationPage from "./pages/InvoiceClassification/InvoiceClassificationPage";
import PackingMaterialsPage from "./pages/PackingMaterialsPage";
import StockOperationsPage from "./pages/StockOperationsPage";
import RecurringJobsPage from "./pages/RecurringJobsPage";
import AuthGuard from "./components/auth/AuthGuard";
import { StatusBar } from "./components/StatusBar";
import { loadConfig, Config, ConfigurationError } from "./config/runtimeConfig";
import ConfigLoadingScreen from "./components/ConfigLoadingScreen";
import ConfigErrorScreen from "./components/ConfigErrorScreen";
import IssuedInvoicesPage from "./pages/customer/IssuedInvoicesPage";
import BankStatementsOverviewPage from "./pages/customer/BankStatementsOverviewPage";
import { setGlobalTokenProvider, setGlobalAuthRedirectHandler, clearTokenCache } from "./api/client";
import { apiRequest } from "./auth/msalConfig";
import { InteractionRequiredAuthError } from "@azure/msal-browser";
import { isE2ETestMode, getE2EAccessToken } from "./auth/e2eAuth";
import { ToastProvider } from "./contexts/ToastContext";
import { LoadingProvider } from "./contexts/LoadingContext";
import { PlanningListProvider } from "./contexts/PlanningListContext";
import { PurchasePlanningListProvider } from "./contexts/PurchasePlanningListContext";
import { ChangelogProvider } from "./contexts/ChangelogContext";
import { GlobalLoadingIndicator } from "./components/GlobalLoadingIndicator";
import { AppInitializer } from "./components/AppInitializer";
import { ChangelogToaster, ChangelogModalContainer } from "./features/changelog";
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
  const [error, setError] = useState<ConfigurationError | Error | null>(null);

  useEffect(() => {
    const initializeApp = async () => {
      try {
        console.log("🚀 Initializing Anela Heblo application...");

        // Load configuration from backend /api/configuration endpoint
        const appConfig = await loadConfig();
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
          console.log("🔐 Setting up enhanced real authentication token provider");
          setGlobalTokenProvider(async (forceRefresh: boolean = false) => {
            try {
              const accounts = instance.getAllAccounts();
              if (accounts.length === 0) {
                console.log("🔐 No accounts found in MSAL instance");
                return null;
              }

              const account = accounts[0];
              console.log("🔐 Attempting silent token acquisition...");
              
              const response = await instance.acquireTokenSilent({
                ...apiRequest,
                account: account,
                forceRefresh, // Support force refresh
              });

              console.log("✅ Silent token acquisition successful in global provider");
              return response.accessToken;
            } catch (error) {
              console.log("⚠️ Silent token acquisition failed in global provider:", error);
              
              if (error instanceof InteractionRequiredAuthError) {
                console.log("🔐 Interaction required - clearing cache and triggering redirect...");
                
                // Clear token cache before redirect
                clearTokenCache();
                
                // Note: We can't perform redirect here as this is called from API client
                // The 401 interceptor will handle the redirect
                console.log("🔐 Returning null - 401 interceptor will handle redirect");
                return null;
              } else {
                console.error("❌ Non-interaction token error in global provider:", error);
                return null;
              }
            }
          });
        } else {
          console.log(
            "🧪 Using mock authentication - no token provider needed",
          );
        }

        // Set global authentication redirect handler for 401 errors
        if (!isE2ETestMode() && !appConfig.useMockAuth) {
          console.log("🔐 Setting up global authentication redirect handler");
          setGlobalAuthRedirectHandler(() => {
            console.log("🔐 Executing automatic login redirect due to token expiration");
            
            // Clear any existing session data
            sessionStorage.clear();
            
            // Use the MSAL instance to perform login redirect
            instance.loginRedirect({
              ...apiRequest,
              prompt: "select_account", // Show account picker for expired sessions
            }).catch((error) => {
              console.error("❌ Automatic login redirect failed:", error);
              
              // Fallback: redirect to root and let normal auth flow handle it
              window.location.href = "/";
            });
          });
        } else {
          console.log("🧪 Skipping auth redirect handler setup for mock/E2E mode");
        }

        console.log("✅ Application initialized successfully");
      } catch (err) {
        console.error("❌ Critical error during app initialization:", err);

        // Set error for display
        if (err instanceof ConfigurationError) {
          setError(err);
        } else if (err instanceof Error) {
          setError(new ConfigurationError(err.message, err));
        } else {
          setError(new ConfigurationError("Unknown error during application initialization"));
        }
      } finally {
        setLoading(false);
      }
    };

    initializeApp();
  }, []);

  if (loading) {
    return <ConfigLoadingScreen />;
  }

  if (error) {
    return (
      <ConfigErrorScreen
        error={error}
        onRetry={() => window.location.reload()}
      />
    );
  }

  if (!config || !msalInstance) {
    return <ConfigLoadingScreen />;
  }

  return (
    <QueryClientProvider client={queryClient}>
      <LoadingProvider>
        <ToastProvider>
          <ChangelogProvider>
            <PlanningListProvider>
              <PurchasePlanningListProvider>
            <AppInitializer>
            <div className="App min-h-screen" data-testid="app">
              <MsalProvider instance={msalInstance}>
                <Router
                  future={{
                    v7_startTransition: true,
                    v7_relativeSplatPath: true,
                  }}
                >
                  <AuthGuard>
                    <Layout statusBar={<StatusBar />}>
                      <Routes>
                        <Route path="/" element={<Dashboard />} />
                        <Route
                          path="/finance/overview"
                          element={<FinancialOverview />}
                        />
                        <Route
                          path="/finance/bank-statements"
                          element={<BankStatementImportChart />}
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
                          path="/purchase/invoice-classification"
                          element={<InvoiceClassificationPage />}
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
                          path="/manufacturing/batch-planning"
                          element={<BatchPlanningCalculator />}
                        />
                        <Route
                          path="/manufacturing/orders"
                          element={<ManufactureOrderList />}
                        />
                        <Route
                          path="/manufacturing/orders/:id"
                          element={<ManufactureOrderDetail />}
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
                          path="/logistics/inventory"
                          element={<InventoryList />}
                        />
                        <Route
                          path="/manufacturing/inventory"
                          element={<ManufactureInventoryList />}
                        />
                        <Route
                          path="/logistics/transport-boxes"
                          element={<TransportBoxList />}
                        />
                        <Route
                          path="/logistics/receive-boxes"
                          element={<TransportBoxReceive />}
                        />
                        <Route
                          path="/logistics/gift-package-manufacturing"
                          element={<GiftPackageManufacturing />}
                        />
                        <Route
                          path="/logistics/warehouse-statistics"
                          element={<WarehouseStatistics />}
                        />
                        <Route
                          path="/logistics/packing-materials"
                          element={<PackingMaterialsPage />}
                        />
                        <Route
                          path="/automation/invoice-import-statistics"
                          element={<InvoiceImportStatistics />}
                        />
                        <Route
                          path="/automation/background-tasks"
                          element={<BackgroundTasks />}
                        />
                        <Route
                          path="/customer/issued-invoices"
                          element={<IssuedInvoicesPage />}
                        />
                        <Route
                          path="/customer/bank-statements-overview"
                          element={<BankStatementsOverviewPage />}
                        />
                        <Route path="/orgchart" element={<OrgChartPage />} />
                        <Route
                          path="/stock-operations"
                          element={<StockOperationsPage />}
                        />
                        <Route
                          path="/recurring-jobs"
                          element={<RecurringJobsPage />}
                        />
                      </Routes>
                    </Layout>
                  </AuthGuard>
                </Router>
              </MsalProvider>
              <GlobalLoadingIndicator />
              <ChangelogToaster />
              <ChangelogModalContainer />
            </div>
          </AppInitializer>
              </PurchasePlanningListProvider>
            </PlanningListProvider>
          </ChangelogProvider>
        </ToastProvider>
      </LoadingProvider>
    </QueryClientProvider>
  );
}

export default App;
