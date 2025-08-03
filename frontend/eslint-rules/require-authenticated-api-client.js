/**
 * ESLint rule to enforce usage of getAuthenticatedApiClient for API calls
 * Prevents 401 Unauthorized errors in production
 */

module.exports = {
  meta: {
    type: 'problem',
    docs: {
      description: 'enforce usage of getAuthenticatedApiClient for API calls',
      category: 'Possible Errors',
      recommended: true,
    },
    fixable: null,
    schema: [],
    messages: {
      unauthenticatedFetch: 'Use getAuthenticatedApiClient() instead of plain fetch() for API calls to avoid 401 Unauthorized errors',
      missingAuthImport: 'Import getAuthenticatedApiClient from "../client" when making API calls',
      hardcodedQueryKey: 'Use QUERY_KEYS from "../client" instead of hardcoded query keys for consistent caching',
    },
  },

  create(context) {
    let hasAuthImport = false;
    let hasApiCalls = false;
    let hasQueryKeysImport = false;

    return {
      // Check imports
      ImportDeclaration(node) {
        if (node.source.value === '../client' || node.source.value === './client') {
          const specifiers = node.specifiers;
          hasAuthImport = specifiers.some(spec => 
            spec.imported && spec.imported.name === 'getAuthenticatedApiClient'
          );
          hasQueryKeysImport = specifiers.some(spec =>
            spec.imported && spec.imported.name === 'QUERY_KEYS'
          );
        }
      },

      // Check fetch calls
      CallExpression(node) {
        // Check for fetch() calls
        if (node.callee.name === 'fetch') {
          const args = node.arguments;
          if (args.length > 0) {
            const firstArg = args[0];
            
            // Check if it's an API call (contains /api/ or uses config.apiUrl)
            let isApiCall = false;
            
            if (firstArg.type === 'Literal' && typeof firstArg.value === 'string') {
              isApiCall = firstArg.value.includes('/api/');
            } else if (firstArg.type === 'TemplateLiteral') {
              const templateValue = firstArg.quasis.map(q => q.value.raw).join('');
              isApiCall = templateValue.includes('/api/') || 
                         templateValue.includes('${config.apiUrl}') ||
                         templateValue.includes('${apiUrl}');
            } else if (firstArg.type === 'CallExpression' && 
                      firstArg.callee.type === 'MemberExpression' &&
                      firstArg.callee.object.name === 'url' &&
                      firstArg.callee.property.name === 'toString') {
              // url.toString() pattern - likely an API call
              isApiCall = true;
            }
            
            if (isApiCall) {
              hasApiCalls = true;
              
              // Check if it's using authenticated client pattern
              const sourceCode = context.getSourceCode();
              const fileContent = sourceCode.getText();
              const isUsingAuthPattern = fileContent.includes('(apiClient as any).http.fetch') ||
                                       fileContent.includes('apiClient.http.fetch');
              
              if (!isUsingAuthPattern) {
                context.report({
                  node,
                  messageId: 'unauthenticatedFetch',
                });
              }
            }
          }
        }

        // Check for hardcoded query keys in useQuery
        if (node.callee.name === 'useQuery' && node.arguments.length > 0) {
          const configArg = node.arguments[0];
          if (configArg.type === 'ObjectExpression') {
            const queryKeyProp = configArg.properties.find(prop => 
              prop.key && prop.key.name === 'queryKey'
            );
            
            if (queryKeyProp && queryKeyProp.value.type === 'ArrayExpression') {
              const elements = queryKeyProp.value.elements;
              const hasQueryKeysUsage = elements.some(element => 
                element && element.type === 'SpreadElement' &&
                element.argument.type === 'MemberExpression' &&
                element.argument.object.name === 'QUERY_KEYS'
              );
              
              const hasHardcodedStrings = elements.some(element =>
                element && element.type === 'Literal' && typeof element.value === 'string'
              );
              
              if (hasHardcodedStrings && !hasQueryKeysUsage) {
                context.report({
                  node: queryKeyProp,
                  messageId: 'hardcodedQueryKey',
                });
              }
            }
          }
        }
      },

      // Check at the end of the file
      'Program:exit'() {
        if (hasApiCalls && !hasAuthImport) {
          context.report({
            node: context.getSourceCode().ast,
            messageId: 'missingAuthImport',
          });
        }
      },
    };
  },
};