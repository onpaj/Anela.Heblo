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
          const hasAuthenticatedClient = fileContent.includes(
            "getAuthenticatedApiClient()",
          );
          const isUsingAuthenticatedPattern =
            fileContent.includes("(apiClient as any).http.fetch") ||
            fileContent.includes("apiClient.http.fetch");

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
          "Use getAuthenticatedApiClient() instead of plain fetch() for API calls.\n" +
          "Example:\n" +
          "  const apiClient = getAuthenticatedApiClient();\n" +
          "  const response = await (apiClient as any).http.fetch(fullUrl, {...});",
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
        const hasAuthenticatedClient = content.includes(
          "getAuthenticatedApiClient",
        );
        const hasPlainFetch =
          content.includes("fetch(") &&
          !content.includes("(apiClient as any).http.fetch") &&
          !content.includes("apiClient.http.fetch");

        if (!hasAuthenticatedClient) {
          violations.push({
            file: file.replace(apiHooksDir, ""),
            reason: "Missing getAuthenticatedApiClient() import and usage",
          });
        }

        if (hasPlainFetch) {
          violations.push({
            file: file.replace(apiHooksDir, ""),
            reason: "Using plain fetch() instead of authenticated API client",
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
          '1. Import getAuthenticatedApiClient from "../client"\n' +
          "2. Use apiClient.http.fetch() instead of plain fetch()\n" +
          "3. Follow the pattern from useCatalog.ts",
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
