# Architecture Review: Users Feature
**Date:** 2025-08-24  
**Reviewer:** Architecture Team  
**Feature Path:** `backend/src/Anela.Heblo.Application/features/Users/`

## Executive Summary

The Users feature is minimal and incomplete, consisting only of a CurrentUserService for extracting user context from claims. While what exists is implemented correctly, the feature lacks essential user management capabilities and proper module registration. It serves as a utility service rather than a complete feature module.

**Overall Score: 4/10** - Minimal implementation missing core functionality

## Feature Overview

**Purpose:** User context management and user-related operations
**Current Implementation:** Single service for extracting current user information from claims
**Missing:** User profiles, management, persistence, and most standard user operations

## Architecture Analysis

### Current Implementation

```
Users/
└── CurrentUserService.cs    # Only file - extracts user from HTTP context
```

**What's Missing:**
- `UsersModule.cs` - No module registration
- Domain models (User, UserProfile, etc.)
- Repository interfaces and implementations
- Handlers for user operations
- Contracts/DTOs for user-related requests

### Strengths ✅

#### 1. Clean Service Implementation
```csharp
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public Guid GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("sub") 
                         ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
                         
        if (userIdClaim?.Value != null && Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        
        throw new UnauthorizedAccessException("User ID not found in claims");
    }
    
    public string GetCurrentUserName()
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirst("name")?.Value 
               ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Name)?.Value 
               ?? "Unknown User";
    }
    
    public string GetCurrentUserEmail()
    {
        return _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value 
               ?? _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value 
               ?? string.Empty;
    }
}
```

**Good Aspects:**
- ✅ Proper null checking and defensive programming
- ✅ Fallback logic for different claim types
- ✅ Clear exception handling for missing user context
- ✅ Support for both OpenID Connect (`sub`, `name`, `email`) and legacy claims

#### 2. Good Interface Design
```csharp
public interface ICurrentUserService
{
    Guid GetCurrentUserId();
    string GetCurrentUserName();
    string GetCurrentUserEmail();
}
```
- Simple, focused interface
- Clear method names and responsibilities

### Critical Issues ❌

#### 1. Missing Module Registration
```csharp
// File doesn't exist: UsersModule.cs
// Service registration is scattered or missing
```
**Problem:** No consistent registration pattern with other features
**Impact:** Service might not be registered properly

#### 2. Incomplete Feature Implementation ❌
**Missing Core Functionality:**
- User profile management
- User preferences/settings
- User role management
- User registration/onboarding
- User deactivation/management
- User audit trail

#### 3. No Persistence Layer ❌
```csharp
// Missing:
// - User entity/aggregate
// - User repository
// - Database mappings
// - User-related database operations
```

#### 4. No User Context Caching ❌
```csharp
public Guid GetCurrentUserId()
{
    // Parses claims on every call - inefficient
    var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("sub");
}
```
**Problem:** Repeated claim parsing for same request
**Solution:** Cache user context per HTTP request

### Moderate Issues 🟡

#### 1. Limited Error Information ⚠️
```csharp
throw new UnauthorizedAccessException("User ID not found in claims");
```
**Issue:** Generic error doesn't help with debugging claim issues
**Improvement:** Add more detailed error information

#### 2. No User Validation ⚠️
```csharp
if (userIdClaim?.Value != null && Guid.TryParse(userIdClaim.Value, out var userId))
{
    return userId;
}
```
**Missing:** Validation that user exists in system, is active, etc.

#### 3. Hardcoded Claim Names ⚠️
```csharp
.FindFirst("sub")  // OpenID Connect standard
.FindFirst("name") // But hardcoded
.FindFirst("email")
```
**Recommendation:** Make claim names configurable

## Missing Architecture Components

### Should Exist But Don't

#### 1. User Aggregate
```csharp
public class User : AggregateRoot<Guid>
{
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public UserStatus Status { get; private set; }
    public UserProfile Profile { get; private set; }
    public List<UserRole> Roles { get; private set; }
    public DateTime LastLoginAt { get; private set; }
    
    // Business methods for user management
    public void UpdateProfile(UserProfile newProfile) { /* ... */ }
    public void DeactivateUser(string reason) { /* ... */ }
    public void AddRole(UserRole role) { /* ... */ }
}
```

#### 2. User Repository
```csharp
public interface IUserRepository : IRepository<User, Guid>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IEnumerable<User>> GetActiveUsersAsync(CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);
}
```

#### 3. User Management Handlers
```csharp
public class GetUserProfileHandler : IRequestHandler<GetUserProfileRequest, GetUserProfileResponse> { }
public class UpdateUserProfileHandler : IRequestHandler<UpdateUserProfileRequest, UpdateUserProfileResponse> { }
public class DeactivateUserHandler : IRequestHandler<DeactivateUserRequest, DeactivateUserResponse> { }
```

## Recommendations

### Immediate Actions (P0)

#### 1. Create UsersModule
```csharp
public static class UsersModule
{
    public static IServiceCollection AddUsersModule(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        
        return services;
    }
}
```

#### 2. Add Request-Level Caching
```csharp
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IMemoryCache _memoryCache;
    
    public Guid GetCurrentUserId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) throw new InvalidOperationException("No HTTP context available");
        
        // Cache per request
        var cacheKey = $"CurrentUserId_{httpContext.TraceIdentifier}";
        return _memoryCache.GetOrCreate(cacheKey, factory =>
        {
            factory.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return ExtractUserIdFromClaims(httpContext.User);
        });
    }
    
    private Guid ExtractUserIdFromClaims(ClaimsPrincipal user)
    {
        var userIdClaim = user?.FindFirst("sub") ?? user?.FindFirst(ClaimTypes.NameIdentifier);
        
        if (userIdClaim?.Value != null && Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return userId;
        }
        
        throw new UnauthorizedAccessException($"User ID not found in claims. Available claims: {string.Join(", ", user?.Claims?.Select(c => c.Type) ?? Array.Empty<string>())}");
    }
}
```

#### 3. Make Claim Names Configurable
```csharp
public class UserClaimsOptions
{
    public string UserIdClaimType { get; set; } = "sub";
    public string UserNameClaimType { get; set; } = "name";
    public string EmailClaimType { get; set; } = "email";
    public string[] FallbackUserIdClaimTypes { get; set; } = { ClaimTypes.NameIdentifier };
    public string[] FallbackNameClaimTypes { get; set; } = { ClaimTypes.Name };
    public string[] FallbackEmailClaimTypes { get; set; } = { ClaimTypes.Email };
}
```

### Short-term Improvements (P1)

#### 1. Add User Entity and Repository
```csharp
public class User : AggregateRoot<Guid>
{
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    
    public void UpdateLastLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        AddDomainEvent(new UserLoggedInEvent(Id, Email));
    }
    
    public void Deactivate(string reason)
    {
        Status = UserStatus.Inactive;
        AddDomainEvent(new UserDeactivatedEvent(Id, reason));
    }
}
```

#### 2. Add User Profile Management
```csharp
public class GetUserProfileHandler : IRequestHandler<GetUserProfileRequest, GetUserProfileResponse>
{
    public async Task<GetUserProfileResponse> Handle(GetUserProfileRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetCurrentUserId();
        var user = await _userRepository.GetByIdAsync(currentUserId, cancellationToken);
        
        if (user == null)
        {
            throw new NotFoundException($"User with ID {currentUserId} not found");
        }
        
        return new GetUserProfileResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Status = user.Status,
            LastLoginAt = user.LastLoginAt
        };
    }
}
```

### Long-term Improvements (P2)

1. **User Preferences System** - Settings, themes, language preferences
2. **Role-Based Authorization** - User roles and permissions
3. **User Activity Tracking** - Login history, action audit trails
4. **Profile Picture Management** - Avatar upload and management
5. **User Onboarding Workflow** - First-time user experience

## Testing Strategy

### Current Gaps
- No unit tests for CurrentUserService
- No tests for claim parsing logic
- No integration tests for user context

### Recommended Tests
```csharp
public class CurrentUserServiceTests
{
    [Test]
    public void GetCurrentUserId_WithValidSubClaim_ReturnsCorrectId()
    {
        // Test with OpenID Connect 'sub' claim
        var userId = Guid.NewGuid();
        var claims = new[] { new Claim("sub", userId.ToString()) };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);
        
        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        
        var service = new CurrentUserService(accessor.Object);
        var result = service.GetCurrentUserId();
        
        Assert.Equal(userId, result);
    }
    
    [Test]
    public void GetCurrentUserId_WithNoUserIdClaim_ThrowsUnauthorizedException()
    {
        // Test exception handling
    }
}
```

## Conclusion

The Users feature is severely underdeveloped, serving only as a utility service for extracting user context from claims. While the CurrentUserService is implemented correctly, the feature lacks essential user management capabilities expected in a production application.

**Current State:** Utility service only
**Missing:** 95% of typical user management functionality
**Risk Level:** Medium - Current functionality works but insufficient for production

**Key Actions:**
1. **Immediate**: Create proper module registration (1 hour)
2. **Short-term**: Add user entity and repository (2-3 days)
3. **Medium-term**: Implement user profile management (1 week)
4. **Long-term**: Full user management system (2-3 weeks)

**Recommendation:** Current implementation is sufficient for basic authentication scenarios, but needs significant expansion for user management features. Consider this when planning user-related functionality in other modules.