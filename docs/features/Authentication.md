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

- Aplikace používá standardní knihovnu pro přihlášení (např. MSAL).
- Přihlášení probíhá pomocí popup okna.
- Použitý flow: Authorization Code s PKCE.
- Po přihlášení získává frontend access token pro backend API.
- Token se předává pomocí HTTP hlavičky `Authorization` (Bearer).
- React SPA je registrováno jako samostatná **public client** aplikace v Entra ID.

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

## 7. Shrnutí

| Oblast                  | Přístup                                   |
|--------------------------|-------------------------------------------|
| Autentizace              | Microsoft Entra ID                        |
| Tokeny                   | JWT (Bearer)                              |
| Frontend login           | Authorization Code flow s PKCE (popup)   |
| Robotické účty           | Client Credentials flow                   |
| Role                     | admin, robot                              |
| Autorizace               | Role-based + Policy-based                 |
| Validace tokenu          | Pomocí knihovny (ne manuálně)             |
| Ochrana endpointů        | Výchozí stav `[Authorize]`                |
| Hostování                | `heblo.pajgrt.cz`, SPA jako statické soubory |

---