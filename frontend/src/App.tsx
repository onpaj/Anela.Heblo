import React, { useState, useEffect } from "react";
import { BrowserRouter as Router, Routes, Route, Outlet, Navigate } from "react-router-dom";
import { MsalProvider } from "@azure/msal-react";
import { PublicClientApplication, EventType, AccountInfo } from "@azure/msal-browser";
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
import TransportBoxReceivePage from "./components/pages/TransportBoxReceive";
import GiftPackageManufacturing from "./components/pages/GiftPackageManufacturing";
import WarehouseStatistics from "./components/pages/WarehouseStatistics";
import InventoryList from "./components/pages/InventoryList";
import ManufactureInventoryList from "./components/pages/ManufactureInventoryList";
import MaterialContainerList from "./components/pages/MaterialContainerList";
import ManufacturedInventoryPage from "./components/pages/ManufacturedInventoryPage";
import ManufactureOrderList from "./components/manufacture/pages/ManufactureOrderList";
import ManufactureOrderDetail from "./components/manufacture/pages/ManufactureOrderDetail";
import InvoiceImportStatistics from "./components/pages/automation/InvoiceImportStatistics";
import BackgroundTasks from "./components/pages/automation/BackgroundTasks";
import MeetingTasksPage from "./components/pages/automation/MeetingTasksPage";
import MeetingTaskDetailPage from "./components/pages/automation/MeetingTaskDetailPage";
import OrgChartPage from "./pages/OrgChartPage";
import FeatureFlagsAdminPage from "./pages/FeatureFlagsAdminPage";
import AccessManagementPage from "./pages/AccessManagementPage";
import InvoiceClassificationPage from "./pages/InvoiceClassification/InvoiceClassificationPage";
import PackingMaterialsPage from "./pages/PackingMaterialsPage";
import StockOperationsPage from "./pages/StockOperationsPage";
import RecurringJobsPage from "./pages/RecurringJobsPage";
import KnowledgeBasePage from "./pages/KnowledgeBasePage";
import KnowledgeBaseFeedbackPage from "./pages/KnowledgeBaseFeedbackPage";
import MarketingFeedbackPage from "./pages/MarketingFeedbackPage";
import ArticlesPage from "./pages/ArticlesPage";
import ExpeditionListArchivePage from "./pages/ExpeditionListArchivePage";
import MarketingCalendarPage from "./components/marketing/pages/MarketingCalendarPage";
import PhotobankPage from "./components/marketing/photobank/pages/PhotobankPage";
import PhotobankSettingsPage from "./components/marketing/photobank/pages/PhotobankSettingsPage";
import AuthGuard from "./components/auth/AuthGuard";
import { ACCESS_ROUTES } from "./auth/accessMatrix.generated";
import { RequireAccess } from "./components/auth/RequireAccess";
import { StatusBar } from "./components/StatusBar";
import { loadConfig, Config } from "./config/runtimeConfig";
import IssuedInvoicesPage from "./pages/customer/IssuedInvoicesPage";
import DataQualityPage from "./pages/customer/DataQualityPage";
import BankStatementsOverviewPage from "./pages/customer/BankStatementsOverviewPage";
import SmartsuppChatsPage from "./components/customer-support/smartsupp/pages/SmartsuppChatsPage";
import ExpeditionSettingsPage from "./pages/customer/ExpeditionSettingsPage";
import { setGlobalTokenProvider, setGlobalAuthRedirectHandler, clearTokenCache, TokenResult } from "./api/client";
import { UserStorage } from "./auth/userStorage";
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
import { FeatureFlagProvider } from "./features/feature-flags/FeatureFlagProvider";
import { PermissionsProvider } from "./auth/PermissionsContext";
import { OpenFeatureProvider } from "@openfeature/react-sdk";
import LeafletGeneratorPage from "./features/leaflet-generator/LeafletGeneratorPage";
import TerminalLayout from "./components/terminal/TerminalLayout";
import TerminalHome from "./components/terminal/TerminalHome";
import TransportBoxCheck from "./components/terminal/TransportBoxCheck";
import TransportBoxReceive from "./components/terminal/TransportBoxReceive";
import ComingSoonPage from "./components/terminal/ComingSoonPage";
import BoxFillWorkflow from "./components/terminal/box-fill/BoxFillWorkflow";
import LotIdentificationHome from "./components/terminal/lot-identification/LotIdentificationHome";
import PoPickStep from "./components/terminal/lot-identification/PoPickStep";
import PoLinePickStep from "./components/terminal/lot-identification/PoLinePickStep";
import LotEntryStep from "./components/terminal/lot-identification/LotEntryStep";
import ContainerScanLoop from "./components/terminal/lot-identification/ContainerScanLoop";
import FinishPoStep from "./components/terminal/lot-identification/FinishPoStep";
import FreeformMaterialStep from "./components/terminal/lot-identification/FreeformMaterialStep";
import BaleniLayout from "./components/baleni/BaleniLayout";
import BaleniHome from "./components/baleni/BaleniHome";
import BaleniPlaceholder from "./components/baleni/BaleniPlaceholder";
import BaleniPacking from "./components/baleni/BaleniPacking";
import { ZasilkyPage } from "./components/baleni/zasilky/ZasilkyPage";
import "./i18n";
import { initAppInsights, getAppInsights, setUserIdentity } from './telemetry/appInsights';
import { AppInsightsProvider } from './telemetry/AppInsightsProvider';

let isRedirecting = false;

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

        initAppInsights(appConfig.aiConnectionString);

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
            cacheLocation: "localStorage" as const,
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

        // Wire Application Insights user context to MSAL authentication events
        instance.addEventCallback((event) => {
          if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
            const account = (event.payload as { account?: AccountInfo }).account;
            const oid = (account?.idTokenClaims as { oid?: string } | undefined)?.oid;
            if (oid) {
              getAppInsights()?.setAuthenticatedUserContext(oid, undefined, true);
              setUserIdentity({ name: account?.name, email: account?.username });
            }
          }
          if (event.eventType === EventType.LOGOUT_SUCCESS) {
            getAppInsights()?.clearAuthenticatedUserContext();
            setUserIdentity(null);
          }
        });

        // For users already signed in (page reload), set context immediately
        const existingAccounts = instance.getAllAccounts();
        if (existingAccounts.length > 0) {
          const existingAccount = existingAccounts[0];
          const oid = (existingAccount.idTokenClaims as { oid?: string } | undefined)?.oid;
          if (oid) {
            getAppInsights()?.setAuthenticatedUserContext(oid, undefined, true);
            setUserIdentity({ name: existingAccount.name, email: existingAccount.username });
          }
        }

        // Clean up stale returnUrl on normal app start (not during MSAL redirect callback)
        const isHandlingRedirect = window.location.search.includes('code=') || window.location.hash.includes('code=');
        if (!isHandlingRedirect) {
          localStorage.removeItem('auth.returnUrl');
        }

        // Set global token provider for API client
        if (isE2ETestMode()) {
          console.log("🧪 Setting up E2E test authentication token provider");
          setGlobalTokenProvider(async (): Promise<TokenResult | null> => {
            console.log("🎫 Providing E2E test token");
            const token = getE2EAccessToken();
            return token ? { token, expiresOn: null } : null;
          });
        } else if (!appConfig.useMockAuth) {
          console.log("🔐 Setting up enhanced real authentication token provider");
          setGlobalTokenProvider(async (forceRefresh: boolean = false): Promise<TokenResult | null> => {
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
                forceRefresh,
              });

              console.log("✅ Silent token acquisition successful in global provider");
              return { token: response.accessToken, expiresOn: response.expiresOn };
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
            if (isRedirecting) return;
            isRedirecting = true;

            console.log("🔐 Executing automatic login redirect due to token expiration");

            // Save current URL to localStorage so it can be restored after re-login
            const returnUrl = window.location.pathname + window.location.search;
            if (returnUrl && returnUrl !== '/') {
              localStorage.setItem('auth.returnUrl', returnUrl);
            }

            // Clear app-level session data (preserve MSAL PKCE verifier for auth code exchange)
            UserStorage.clearUserInfo();
            clearTokenCache();

            // Attempt silent SSO first — if Azure AD has a valid session the user won't see any UI.
            // Only fall back to select_account if the silent attempt itself fails.
            instance.loginRedirect({
              ...apiRequest,
              prompt: "none",
            }).catch(() => {
              console.warn("🔐 Silent SSO redirect failed, falling back to account picker");
              instance.loginRedirect({
                ...apiRequest,
                prompt: "select_account",
              }).catch((error) => {
                console.error("❌ Automatic login redirect failed:", error);
                isRedirecting = false;

                // Fallback: redirect to root and let normal auth flow handle it
                window.location.href = "/";
              });
            });
          });
        } else {
          console.log("🧪 Skipping auth redirect handler setup for mock/E2E mode");
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

  const guard = (path: string, element: React.ReactNode) => (
    <RequireAccess requiredRole={ACCESS_ROUTES[path]}>{element}</RequireAccess>
  );

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
    <OpenFeatureProvider>
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
                    <PermissionsProvider isAuthenticated={true}>
                    <FeatureFlagProvider>
                    <AppInsightsProvider>
                    <Routes>
                      {/* Mobile terminal — no sidebar, no topbar */}
                      <Route path="/terminal" element={<TerminalLayout />}>
                        <Route index element={<TerminalHome />} />
                        <Route path="box-check" element={<TransportBoxCheck />} />
                        <Route path="box-fill" element={<BoxFillWorkflow />} />
                        <Route path="receive" element={<TransportBoxReceive />} />
                        <Route path="stocktake" element={<ComingSoonPage title="Inventura" />} />
                        <Route path="lot-identification">
                          <Route index element={<LotIdentificationHome />} />
                          <Route path="po" element={<PoPickStep />} />
                          <Route path="po/:id" element={<PoLinePickStep />} />
                          <Route path="po/:id/line/:lineId/material/:material/lot" element={<LotEntryStep mode="po" />} />
                          <Route path="po/:id/line/:lineId/material/:material/lot/:lot/scan" element={<ContainerScanLoop mode="po" />} />
                          <Route path="po/:id/finish" element={<FinishPoStep />} />
                          <Route path="freeform" element={<FreeformMaterialStep />} />
                          <Route path="freeform/:material/lot" element={<LotEntryStep mode="freeform" />} />
                          <Route path="freeform/:material/lot/:lot/scan" element={<ContainerScanLoop mode="freeform" />} />
                        </Route>
                      </Route>

                      {/* Balení device module — landscape touch PC, no sidebar */}
                      <Route path="/baleni" element={guard("/baleni", <BaleniLayout />)}>
                        <Route index element={<BaleniHome />} />
                        <Route path="baleni" element={<BaleniPacking />} />
                        <Route path="zasilky" element={<ZasilkyPage />} />
                        <Route path="statistiky" element={<BaleniPlaceholder title="Statistiky" />} />
                      </Route>

                      {/* Desktop app — full Layout with sidebar (pathless layout route) */}
                      <Route element={<Layout statusBar={<StatusBar />}><Outlet /></Layout>}>
                        <Route path="/" element={<Dashboard />} />
                        <Route path="/finance/overview" element={guard("/finance/overview", <FinancialOverview />)} />
                        <Route path="/finance/bank-statements" element={<BankStatementImportChart />} />
                        <Route path="/analytics/product-margin-summary" element={guard("/analytics/product-margin-summary", <ProductMarginSummary />)} />
                        <Route path="/catalog" element={guard("/catalog", <CatalogList />)} />
                        <Route path="/purchase/orders" element={guard("/purchase/orders", <PurchaseOrderList />)} />
                        <Route path="/purchase/stock-analysis" element={guard("/purchase/stock-analysis", <PurchaseStockAnalysis />)} />
                        <Route path="/purchase/invoice-classification" element={<InvoiceClassificationPage />} />
                        <Route path="/manufacturing/stock-analysis" element={guard("/manufacturing/stock-analysis", <ManufacturingStockAnalysis />)} />
                        <Route path="/manufacturing/output" element={guard("/manufacturing/output", <ManufactureOutput />)} />
                        <Route path="/manufacturing/batch-calculator" element={<ManufactureBatchCalculator />} />
                        <Route path="/manufacturing/batch-planning" element={guard("/manufacturing/batch-planning", <BatchPlanningCalculator />)} />
                        <Route path="/manufacturing/orders" element={guard("/manufacturing/orders", <ManufactureOrderList />)} />
                        <Route path="/manufacturing/orders/:id" element={<ManufactureOrderDetail />} />
                        <Route path="/products/margins" element={guard("/products/margins", <ProductMarginsList />)} />
                        <Route path="/journal" element={guard("/journal", <JournalList />)} />
                        <Route path="/marketing/calendar" element={guard("/marketing/calendar", <MarketingCalendarPage />)} />
                        <Route path="/marketing/photobank" element={guard("/marketing/photobank", <PhotobankPage />)} />
                        <Route path="/marketing/photobank/settings" element={<PhotobankSettingsPage />} />
                        <Route path="/leaflet-generator" element={guard("/leaflet-generator", <LeafletGeneratorPage />)} />
                        <Route path="/journal/new" element={<JournalEntryNew />} />
                        <Route path="/journal/:id/edit" element={<JournalEntryEdit />} />
                        <Route path="/logistics/inventory" element={guard("/logistics/inventory", <InventoryList />)} />
                        <Route path="/manufacturing/inventory" element={guard("/manufacturing/inventory", <ManufactureInventoryList />)} />
                        <Route path="/manufacturing/product-inventory" element={guard("/manufacturing/product-inventory", <ManufacturedInventoryPage />)} />
                        <Route path="/manufacturing/material-containers" element={guard("/manufacturing/material-containers", <MaterialContainerList />)} />
                        <Route path="/logistics/transport-boxes" element={<TransportBoxList />} />
                        <Route path="/logistics/receive-boxes" element={<TransportBoxReceivePage />} />
                        <Route path="/logistics/gift-package-manufacturing" element={<GiftPackageManufacturing />} />
                        <Route path="/logistics/warehouse-statistics" element={<WarehouseStatistics />} />
                        <Route path="/logistics/packing-materials" element={<PackingMaterialsPage />} />
                        <Route path="/logistics/expedition-archive" element={guard("/logistics/expedition-archive", <ExpeditionListArchivePage />)} />
                        <Route path="/automation/invoice-import-statistics" element={<InvoiceImportStatistics />} />
                        <Route path="/automation/background-tasks" element={<BackgroundTasks />} />
                        <Route path="/automation/meeting-tasks" element={guard("/automation/meeting-tasks", <MeetingTasksPage />)} />
                        <Route path="/automation/meeting-tasks/:id" element={<MeetingTaskDetailPage />} />
                        <Route path="/customer/issued-invoices" element={<IssuedInvoicesPage />} />
                        <Route path="/customer/bank-statements-overview" element={guard("/customer/bank-statements-overview", <BankStatementsOverviewPage />)} />
                        <Route path="/customer/smartsupp" element={guard("/customer/smartsupp", <SmartsuppChatsPage />)} />
                        <Route path="/customer/expedition-settings" element={<ExpeditionSettingsPage />} />
                        <Route path="/customer/cooling" element={<Navigate to="/customer/expedition-settings?tab=cooling" replace />} />
                        <Route path="/orgchart" element={<OrgChartPage />} />
                        <Route path="/stock-up-operations" element={guard("/stock-up-operations", <StockOperationsPage />)} />
                        <Route path="/recurring-jobs" element={<RecurringJobsPage />} />
                        <Route path="/knowledge-base" element={guard("/knowledge-base", <KnowledgeBasePage />)} />
                        <Route path="/knowledge-base/feedback" element={<KnowledgeBaseFeedbackPage />} />
                        <Route path="/marketing/feedback" element={<MarketingFeedbackPage />} />
                        <Route path="/articles" element={guard("/articles", <ArticlesPage />)} />
                        <Route path="/automation/data-quality" element={guard("/automation/data-quality", <DataQualityPage />)} />
                        <Route path="/admin/feature-flags" element={guard("/admin/feature-flags", <FeatureFlagsAdminPage />)} />
                        <Route
                          path="/admin/access"
                          element={
                            <RequireAccess requiredRole="administration.read">
                              <AccessManagementPage />
                            </RequireAccess>
                          }
                        />
                      </Route>
                    </Routes>
                    </AppInsightsProvider>
                    </FeatureFlagProvider>
                    </PermissionsProvider>
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
    </OpenFeatureProvider>
  );
}

export default App;
