/**
 * Test to ensure all API calls use authenticated client
 * Prevents 401 Unauthorized errors in production
 */

import fs from "fs";
import path from "path";

describe("Authenticated API Usage", () => {
  const apiHooksDir = path.join(__dirname, "../hooks");
  const srcDir = path.join(__dirname, "../../");

  // Get all TypeScript files that might contain API calls
  const getTypeScriptFiles = (dir: string): string[] => {
    const files: string[] = [];

    const readDirRecursive = (currentDir: string) => {
      const items = fs.readdirSync(currentDir);

      items.forEach((item) => {
        const fullPath = path.join(currentDir, item);
        const stat = fs.statSync(fullPath);

        if (
          stat.isDirectory() &&
          !item.startsWith(".") &&
          item !== "node_modules"
        ) {
          readDirRecursive(fullPath);
        } else if (item.endsWith(".ts") || item.endsWith(".tsx")) {
          files.push(fullPath);
        }
      });
    };

    readDirRecursive(dir);
    return files;
  };

  it("should not use plain fetch() calls for API endpoints", () => {
    const files = getTypeScriptFiles(srcDir);
    const violations: Array<{ file: string; line: number; content: string }> =
      [];

    files.forEach((file) => {
      const content = fs.readFileSync(file, "utf-8");
      const lines = content.split("\n");

      lines.forEach((line, index) => {
        const trimmedLine = line.trim();

        // Skip test files
        if (file.includes("test") || file.includes("spec")) {
          return;
        }

        // Skip comments
        if (trimmedLine.startsWith("//") || trimmedLine.startsWith("*")) {
          return;
        }

        // Check for fetch calls to API endpoints
        // Using template string variables (checking for interpolation syntax)
        const hasTemplateInterpolation = /\$\{(config\.apiUrl|apiUrl)\}/.test(
          trimmedLine,
        );
        if (
          trimmedLine.includes("fetch(") &&
          (trimmedLine.includes("/api/") || hasTemplateInterpolation)
        ) {
          // Allow if it's using authenticated client pattern
          const fileContent = fs.readFileSync(file, "utf-8");
          const hasAuthenticatedClient =
            fileContent.includes("getAuthenticatedApiClient()") ||
            fileContent.includes("getAuthenticatedFetch");
          const isUsingAuthenticatedPattern =
            fileContent.includes("getAuthenticatedFetch");

          if (!hasAuthenticatedClient || !isUsingAuthenticatedPattern) {
            violations.push({
              file: file.replace(srcDir, ""),
              line: index + 1,
              content: trimmedLine,
            });
          }
        }
      });
    });

    if (violations.length > 0) {
      const errorMessage = violations
        .map((v) => `${v.file}:${v.line} - ${v.content}`)
        .join("\n");

      throw new Error(
        `Found ${violations.length} unauthenticated API calls:\n${errorMessage}\n\n` +
          "Use getAuthenticatedApiClient() or getAuthenticatedFetch() for API calls.\n" +
          "Example (for endpoints where you need to check specific status codes):\n" +
          "  import { getApiBaseUrl, getAuthenticatedFetch } from '../client';\n" +
          "  const response = await getAuthenticatedFetch()(url, { method: 'POST', ... });",
      );
    }
  });

  it("should use getAuthenticatedApiClient() for all API hooks", () => {
    const hookFiles = getTypeScriptFiles(apiHooksDir);
    const violations: Array<{ file: string; reason: string }> = [];

    hookFiles.forEach((file) => {
      const content = fs.readFileSync(file, "utf-8");

      // Skip test files
      if (file.includes("test") || file.includes("spec")) {
        return;
      }

      // Check if file contains API calls
      const hasApiCalls =
        content.includes("/api/") &&
        (content.includes("fetch(") || content.includes("useQuery"));

      if (hasApiCalls) {
        const hasAuthenticatedClient =
          content.includes("getAuthenticatedApiClient") ||
          content.includes("getAuthenticatedFetch") ||
          content.includes("smartsuppClient");
        const hasPlainFetch =
          content.includes("fetch(") &&
          !content.includes("(apiClient as any).http.fetch") &&
          !content.includes("apiClient.http.fetch") &&
          !content.includes("getAuthenticatedFetch");

        if (!hasAuthenticatedClient) {
          violations.push({
            file: file.replace(apiHooksDir, ""),
            reason: "Missing getAuthenticatedApiClient() or getAuthenticatedFetch() import and usage",
          });
        }

        if (hasPlainFetch) {
          violations.push({
            file: file.replace(apiHooksDir, ""),
            reason: "Using plain fetch() instead of authenticated API client or getAuthenticatedFetch()",
          });
        }
      }
    });

    if (violations.length > 0) {
      const errorMessage = violations
        .map((v) => `${v.file} - ${v.reason}`)
        .join("\n");

      throw new Error(
        `Found ${violations.length} API hooks with authentication issues:\n${errorMessage}\n\n` +
          "All API hooks should:\n" +
          '1. Import getAuthenticatedApiClient or getAuthenticatedFetch from "../client"\n' +
          "2. Use getAuthenticatedApiClient() for typed calls, or getAuthenticatedFetch() for status-code branching\n" +
          "3. Follow the pattern from useCatalog.ts or useArticles.ts",
      );
    }
  });

  // This test guards against regressions in hooks that have already been migrated to
  // getApiBaseUrl() + getAuthenticatedFetch(). Pre-existing violations in other hooks
  // are tracked as tech debt — extend MIGRATED_HOOKS below as each hook is cleaned up.
  it("should not use (as any) type casting patterns for API clients", () => {
    // Hooks that have been fully migrated away from (apiClient as any) patterns.
    // Add new hook file names here (basename only) when they are migrated.
    const MIGRATED_HOOKS = new Set(["useArticles.ts"]);

    const hookFiles = getTypeScriptFiles(apiHooksDir);
    const violations: Array<{ file: string; line: number; content: string }> =
      [];

    hookFiles.forEach((file) => {
      const content = fs.readFileSync(file, "utf-8");
      const lines = content.split("\n");
      const basename = path.basename(file);

      // Skip test files and hooks not yet migrated
      if (file.includes("test") || file.includes("spec")) return;
      if (!MIGRATED_HOOKS.has(basename)) return;

      lines.forEach((line, index) => {
        const trimmedLine = line.trim();

        // Skip comments
        if (trimmedLine.startsWith("//") || trimmedLine.startsWith("*")) {
          return;
        }

        // Check for forbidden (as any) patterns
        if (
          trimmedLine.includes("(apiClient as any)") ||
          trimmedLine.includes("as any).http") ||
          trimmedLine.includes("as any).baseUrl")
        ) {
          violations.push({
            file: file.replace(apiHooksDir, ""),
            line: index + 1,
            content: trimmedLine,
          });
        }
      });
    });

    if (violations.length > 0) {
      const errorMessage = violations
        .map((v) => `${v.file}:${v.line} - ${v.content}`)
        .join("\n");

      throw new Error(
        `Found ${violations.length} forbidden (as any) patterns in API hooks:\n${errorMessage}\n\n` +
          "Use getApiBaseUrl() and getAuthenticatedFetch() from '../client' instead.\n" +
          "Import: import { getApiBaseUrl, getAuthenticatedFetch } from '../client'",
      );
    }
  });

  it("should use consistent query keys with QUERY_KEYS", () => {
    const hookFiles = getTypeScriptFiles(apiHooksDir);
    const violations: Array<{ file: string; line: number; content: string }> =
      [];

    hookFiles.forEach((file) => {
      const content = fs.readFileSync(file, "utf-8");
      const lines = content.split("\n");

      // Skip test files
      if (file.includes("test") || file.includes("spec")) {
        return;
      }

      // Find queryKey declarations that span multiple lines
      for (let i = 0; i < lines.length; i++) {
        const line = lines[i].trim();

        // Check for queryKey: [ pattern (start of queryKey declaration)
        if (line.includes("queryKey:") && line.includes("[")) {
          // Skip if it's a comment
          if (line.startsWith("//") || line.startsWith("*")) {
            continue;
          }

          // Check if this line already contains QUERY_KEYS
          if (line.includes("QUERY_KEYS") || line.includes("...QUERY_KEYS")) {
            continue;
          }

          // Look ahead to find the complete queryKey array (up to 10 lines)
          let queryKeyBlock = line;
          let j = i + 1;
          let foundClosingBracket = line.includes("]");
          
          while (j < lines.length && j < i + 10 && !foundClosingBracket) {
            const nextLine = lines[j].trim();
            queryKeyBlock += " " + nextLine;
            if (nextLine.includes("]")) {
              foundClosingBracket = true;
            }
            j++;
          }

          // Check if the complete queryKey block uses QUERY_KEYS
          if (!queryKeyBlock.includes("QUERY_KEYS") && !queryKeyBlock.includes("...QUERY_KEYS")) {
            violations.push({
              file: file.replace(apiHooksDir, ""),
              line: i + 1,
              content: line,
            });
          }
        }
      }
    });

    if (violations.length > 0) {
      const errorMessage = violations
        .map((v) => `${v.file}:${v.line} - ${v.content}`)
        .join("\n");

      throw new Error(
        `Found ${violations.length} hardcoded query keys:\n${errorMessage}\n\n` +
          'Use QUERY_KEYS from "../client" for consistent caching:\n' +
          'Example: queryKey: [...QUERY_KEYS.catalog, "materials", searchTerm]',
      );
    }
  });
});
