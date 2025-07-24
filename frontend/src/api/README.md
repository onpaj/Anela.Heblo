# API Client

This directory contains the auto-generated TypeScript API client and TanStack Query integration for the Anela Heblo backend API.

## Structure

- `generated/` - Auto-generated API client from backend OpenAPI spec (do not edit manually)
- `client.ts` - API client configuration and constants
- `queryClient.ts` - TanStack Query client setup
- `hooks.ts` - React hooks for API calls using TanStack Query

## Usage

### Basic Query Hook

```typescript
import { useWeatherQuery } from '../api/hooks';

const MyComponent = () => {
  const { data, isLoading, error } = useWeatherQuery();
  
  if (isLoading) return <div>Loading...</div>;
  if (error) return <div>Error: {error.message}</div>;
  
  return <div>{data?.map(item => /* render item */)}</div>;
};
```

### Adding New Query Hooks

1. Add query key to `QUERY_KEYS` in `client.ts`:
```typescript
export const QUERY_KEYS = {
  weather: ['weather'] as const,
  users: ['users'] as const, // New query key
} as const;
```

2. Create hook in `hooks.ts`:
```typescript
export const useUsersQuery = () => {
  return useQuery({
    queryKey: QUERY_KEYS.users,
    queryFn: () => apiClient.getUsers(),
    ...DEFAULT_QUERY_OPTIONS,
  });
};
```

### Mutations

```typescript
export const useCreateUserMutation = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (userData: CreateUserRequest) => apiClient.createUser(userData),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.users });
    },
  });
};
```

## Client Generation

The TypeScript client is automatically generated when you build the backend API project in Debug mode. The generation is configured in `backend/src/Anela.Heblo.API/nswag.frontend.json`.

**Manual generation:**
```bash
cd backend/src/Anela.Heblo.API
dotnet build --target GenerateFrontendClientManual
```

## Configuration

- **Base URL**: Configured via `REACT_APP_API_BASE_URL` environment variable
- **Default timeout**: 30 seconds
- **Retry policy**: 3 retries with exponential backoff
- **Cache time**: 10 minutes
- **Stale time**: 5 minutes