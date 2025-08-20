import React from 'react';
import { AlertTriangle } from 'lucide-react';

interface ErrorBoundaryState {
  hasError: boolean;
  error?: Error;
}

interface ErrorBoundaryProps {
  children: React.ReactNode;
  fallbackComponent?: React.ComponentType<{ error?: Error; resetError: () => void }>;
}

class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: React.ErrorInfo) {
    console.error('ErrorBoundary caught an error:', error);
    console.error('Error Info:', errorInfo);
  }

  resetError = () => {
    this.setState({ hasError: false, error: undefined });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallbackComponent) {
        const FallbackComponent = this.props.fallbackComponent;
        return <FallbackComponent error={this.state.error} resetError={this.resetError} />;
      }

      return (
        <div className="w-full max-w-none px-4 sm:px-6 lg:px-8">
          <div className="mb-8 p-6 bg-red-50 border border-red-200 rounded-lg">
            <div className="flex items-center mb-4">
              <AlertTriangle className="w-6 h-6 text-red-500 mr-3" />
              <h3 className="text-red-800 font-medium text-lg">Nastala chyba při načítání komponenty</h3>
            </div>
            <p className="text-red-700 text-sm mb-4">
              Omlouváme se, došlo k neočekávané chybě. Podrobnosti chyby:
            </p>
            <details className="bg-red-100 p-3 rounded text-xs text-red-800 mb-4">
              <summary className="cursor-pointer font-medium">Technické detaily</summary>
              <pre className="mt-2 whitespace-pre-wrap">
                {this.state.error?.message || 'Neznámá chyba'}
                {this.state.error?.stack && (
                  <>
                    {'\n\nStack trace:\n'}
                    {this.state.error.stack}
                  </>
                )}
              </pre>
            </details>
            <button
              onClick={this.resetError}
              className="bg-red-600 hover:bg-red-700 text-white px-4 py-2 rounded text-sm font-medium transition-colors"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

export default ErrorBoundary;