# Heblo – Autentizace a autorizace

Tento dokument definuje, jakým způsobem aplikace Heblo implementuje autentizaci a autorizaci. Cílem je zajistit jednotný přístup, vysokou bezpečnost, podporu lidských i robotických uživatelů, a snadnou rozšiřitelnost.

---

## 1. Technologie a architektura

- **Frontend**: React (SPA)
- **Backend**: .NET 8 (REST API)
- **Identity Provider**: Microsoft Entra ID (dříve Azure AD)
- **Protokoly**:
    - OAuth 2.0 (Authorization Code flow s PKCE, Client Credentials)
    - OpenID Connect (pro ID tokeny a claims)
    - JWT (Bearer tokeny)

---

## 2. Principy autentizace

### 2.1 Lidský uživatel (React SPA)

- Aplikace používá standardní knihovnu pro přihlášení (MSAL).
- Přihlášení probíhá pomocí redirect flow (Authorization Code s PKCE).
- Po přihlášení získává frontend access token pro backend API.
- Token se předává pomocí HTTP hlavičky `Authorization` (Bearer).
- React SPA je registrováno jako samostatná **public client** aplikace v Entra ID.

#### UI/UX specifikace pro přihlášení:

**Anonymní uživatel:**
- Na spodní části sidebaru se zobrazuje login tlačítko
- Kliknutí na tlačítko spustí autentizaci přes Microsoft Entra ID
- Autentizace probíhá pomocí redirect flow (přesměrování na login.microsoftonline.com)

**Přihlášený uživatel:**
- Místo login tlačítka se zobrazují uživatelské iniciály (např. "OP" pro Ondrej Pajgrt)
- Při rozšířeném sidebaru se navíc zobrazuje jméno uživatele a email
- Kliknutí na uživatelský profil otevře menu s možností odhlášení
- Uživatelské informace jsou ukládány do session storage pro persistenci napříč reloady

### 2.2 Robotický uživatel (non-human)

- Roboti používají **client credentials flow**.
- Každý robot má vlastní app registration v Entra ID.
- Tokeny pro roboty jsou získávány pomocí client ID a secretu.
- Roboti používají stejné API jako běžní uživatelé.
- Robotický účet má přiřazenou specifickou roli `"robot"`.

---

## 3. Principy autorizace

### 3.1 Povinná autentizace

- Ve výchozím stavu vyžadují všechny API endpointy platný access token.
- Výjimky (např. `healthz`, veřejné endpointy) musí být explicitně označeny jako anonymní.

### 3.2 Role-based access

- Role jsou definovány v Entra ID v rámci `appRoles`.
- Uživatelé mohou mít roli `"admin"`, `"robot"` nebo žádnou.
- Role jsou přenášeny v JWT tokenu jako standardní claim.

### 3.3 Policy-based access

- Backend definuje autorizační policy (např. `AdminOnly`, `RobotOnly`).
- Tyto policy vycházejí z přítomnosti konkrétních rolí nebo jiných claimů.
- Endpointy mohou být chráněny kombinací `[Authorize]` a policy.

---

## 4. Identity claims

Aplikace pracuje s následujícími informacemi o uživateli:

- Jméno (claim `name`)
- Email (claim `preferred_username`)
- Role (claim `roles`)

Tyto údaje jsou používány k identifikaci, zobrazení ve frontend UI a k autorizaci.

---

## 5. Doména a nasazení

- Produkční prostředí je provozováno na doméně `heblo.pajgrt.cz`.
- Frontend SPA je hostováno jako statické soubory vedle backend API.
- Vývojové prostředí používá `localhost`, se stejnou doménovou strukturou pro API a frontend.

---

## 6. Bezpečnostní zásady

- Všechny přenosy probíhají výhradně přes HTTPS.
- Tokeny jsou krátkodobé a neobnovují se automaticky.
- Při ztrátě tokenu je vyžadováno nové přihlášení.
- Klíče a tajné údaje (např. client secrets) nejsou nikdy verzovány ani ukládány do repozitáře.
- Tokeny nejsou ukládány do localStorage (používá se session nebo memory cache).

---

## 7. Vývojové prostředí a testování

### 7.1 Mock Authentication – Centralizované řešení

Pro zjednodušení lokálního vývoje a testování aplikace implementuje **centralizované mock authentication** na obou stranách:

#### 7.1.1 Backend Mock Authentication

**Aktivace řízená environment variable:**
- **Jedné řízeno pomocí `"UseMockAuth": true/false` v konfiguraci**
- **Nezávisí na konkrétním prostředí** (`Development`, `Production`, atd.)
- **Výchozí hodnota**: `false` - reálná authentication je default
- **Explicitně zapnuto**: `"UseMockAuth": true` - mock authentication aktivní

**Implementace:**
- `MockAuthenticationHandler` (`backend/src/Anela.Heblo.API/Authentication/MockAuthenticationHandler.cs`)
- Automaticky akceptuje jakýkoliv Bearer token bez validace
- Vytváří standardní `ClaimsPrincipal` s mock daty

**Mock uživatel obsahuje:**
- User ID: `mock-user-id`
- Jméno: `Mock User`
- Email: `mock@anela-heblo.com`
- Claims: oid, tid, roles (standardní Entra ID claims)

**Služby registrované pro mock mode:**
- `NoOpTelemetryService` místo `TelemetryService`
- `null` GraphServiceClient pro controllers, které vyžadují Microsoft Graph
- Všechny API endpointy fungují bez skutečné authentication

#### 7.1.2 Frontend Mock Authentication

**Centralizované řešení:**
- Mock authentication je implementováno v `frontend/src/auth/mockAuth.ts`
- **Nikdy** se neřeší v jednotlivých komponentách
- Jednotné místo pro všechny mock authentication operace

**Implementace mock authentication:**
```typescript
// frontend/src/auth/mockAuth.ts
export const mockAuthService = {
  login: () => Promise<AuthResult>,
  logout: () => void,
  getUser: () => MockUser | null,
  isAuthenticated: () => boolean,
  getAccessToken: () => string // Vrací fake Bearer token
};
```

**Bearer Token Flow v mock režimu:**
1. Frontend vygeneruje fake Bearer token (např. `"mock-bearer-token"`)
2. Token se přikládá do všech API volání v `Authorization` hlavičce
3. Backend `MockAuthenticationHandler` token akceptuje bez validace
4. Backend vrací standardní authenticated response

**Aktivace mock mode:**
- **Jediný rozhodující faktor**: `REACT_APP_USE_MOCK_AUTH=true/false`
- **Nezávisí na prostředí** - lze použít v development, test i production
- **Výchozí hodnota**: `false` - reálná authentication je default
- **Detekce probíhá v `frontend/src/config/runtimeConfig.ts`**
- **Přepíná mezi MSAL a mock authentication services**

#### 7.1.3 API Client Integration

**Centralizované použití:**
```typescript
// frontend/src/api/client.ts
const getAuthHeader = async () => {
  if (useMockAuth) {
    return `Bearer ${mockAuthService.getAccessToken()}`;
  } else {
    const token = await msalInstance.acquireTokenSilent(...);
    return `Bearer ${token.accessToken}`;
  }
};
```

**Výhody centralizovaného přístupu:**
- ✅ Komponenty nemusí vědět o mock vs real authentication
- ✅ Jediné místo pro změny mock behavior
- ✅ Konzistentní API volání napříč celou aplikací
- ✅ Snadné přepínání mezi mock a real authentication

### 7.2 Integration Testing

**Test infrastruktura:**
- `Microsoft.AspNetCore.Mvc.Testing` pro integration tests
- `MockAuthenticationHandler` automaticky aktivní v test prostředí
- Testy ověřují funkčnost API bez skutečné authentication
- Testy kontrolují startup aplikace a dependency injection

**Spuštění testů:**
```bash
dotnet test  # Spustí všechny testy včetně mock authentication
```

**Test projekt:** `backend/test/Anela.Heblo.Tests/`

**ApplicationStartupTests ověřují:**
- ✅ Aplikace startuje úspěšně
- ✅ Všechny services jsou správně zaregistrované
- ✅ Controllers jsou resolvable s dependencies
- ✅ Health endpoints jsou dostupné
- ✅ Mock authentication funguje správně

### 7.3 Konfigurace mock authentication

**Mock authentication zapnutý** (development, testing, demo):
```json
Backend (appsettings.json):
{
  "UseMockAuth": true
}

Frontend (.env):
REACT_APP_USE_MOCK_AUTH=true
```

**Real authentication zapnutý** (production, staging):
```json
Backend (appsettings.json):
{
  "UseMockAuth": false
}

Frontend (.env):
REACT_APP_USE_MOCK_AUTH=false
REACT_APP_AZURE_CLIENT_ID=your-client-id
REACT_APP_AZURE_AUTHORITY=https://login.microsoftonline.com/your-tenant-id
```

**DŮLEŽITÉ**: 
- **Environment (`Development`, `Production`, `Automation`) NEROZHODUJE** o mock authentication
- **Pouze `UseMockAuth` environment variable řídí chování**
- **Lze použít mock authentication i v production prostředí** (pro demo účely)
- **Výchozí hodnota je `false`** - real authentication je default

## 8. Shrnutí

| Oblast                           | Přístup                                          |
|----------------------------------|--------------------------------------------------|
| **Produkční autentizace**        | Microsoft Entra ID                               |
| **Tokeny**                       | JWT Bearer tokens                                |
| **Frontend login**               | Authorization Code flow s PKCE (redirect)       |
| **Robotické účty**               | Client Credentials flow                          |
| **Role**                         | admin, robot                                     |
| **Autorizace**                   | Role-based + Policy-based                       |
| **Validace tokenu**              | Pomocí knihovny (ne manuálně)                   |
| **Ochrana endpointů**            | Výchozí stav `[Authorize]`                      |
| **Hostování**                    | `heblo.pajgrt.cz`, SPA jako statické soubory   |
| **Mock Authentication**          | **Centralizované řešení řízené `UseMockAuth` variable** |
| **Mock aktivace**                | **Pouze `UseMockAuth=true/false` rozhoduje** |
| **Mock nezávisí na prostředí**   | **Lze použít v Development, Test, Production** |
| **Backend Mock**                 | **MockAuthenticationHandler - akceptuje jakýkoliv Bearer token** |
| **Frontend Mock**                | **mockAuth.ts - generuje fake Bearer token**    |
| **API Integration**              | **Centralizované v api/client.ts**              |
| **Testing**                      | **ApplicationStartupTests + mock authentication** |
| **Výchozí hodnota**              | **`UseMockAuth=false` - real auth je default**  |
| **Mock token flow**              | **Frontend → fake Bearer token → Backend akceptuje** |

---