# Smartsupp Visitor API — Findings (spiked 2026-05-20)

## GET /v2/visitors/{visitor_id}

**HTTP status:** 200 OK

**Response shape (exact):**
```json
{
  "id": "visE2fgKgEo8B",
  "created_at": "2026-05-11T17:48:37.676Z",
  "updated_at": "2026-05-15T14:07:09.116Z",
  "is_online": false,
  "status": "offline",
  "visits": 8,
  "lang": "cs",
  "avatar": {
    "initials": "HŠ",
    "color": {
      "text": "9b6d40",
      "bg": "e2d6bc"
    }
  },
  "user_agent": "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.4 Mobile/15E148 Safari/604.1",
  "browser": "Safari",
  "browser_version": "26.4",
  "platform": "iPhone",
  "device": "mobile",
  "os": "OS X",
  "location_country": "Czechia",
  "location_country_code": "CZ",
  "location_city": null,
  "referrer": "https://www.google.com/",
  "page_url": null,
  "page_title": null,
  "variables": null
}
```

Second sample visitor (visitorId `vi-w5VXkpGrF`) for cross-reference:
```json
{
  "id": "vi-w5VXkpGrF",
  "created_at": "2026-05-15T09:00:44.319Z",
  "updated_at": "2026-05-18T06:36:21.130Z",
  "is_online": false,
  "status": "offline",
  "visits": 3,
  "lang": "cs",
  "avatar": {
    "initials": "A",
    "color": { "text": "5f5f9e", "bg": "c1c1f7" }
  },
  "user_agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:150.0) Gecko/20100101 Firefox/150.0",
  "browser": "Firefox",
  "browser_version": "150.0",
  "platform": "Microsoft Windows",
  "device": "desktop",
  "os": "Windows 10.0",
  "location_country": "United States",
  "location_country_code": "US",
  "location_city": "Los Angeles",
  "referrer": "https://www.google.com/",
  "page_url": null,
  "page_title": null,
  "variables": null
}
```

**Fields relevant for Phase 3:**
- `os`: present as `os` (string, e.g. `"OS X"`, `"Windows 10.0"`)
- `browser`: present as `browser` (string, e.g. `"Safari"`, `"Firefox"`)
- `user_agent`: present as `user_agent` (full UA string)
- visits count: present as `visits` (integer)
- `browser_version`: present as `browser_version` (string)
- `platform`: present as `platform` (string, e.g. `"iPhone"`, `"Microsoft Windows"`)
- `device`: present as `device` (string, e.g. `"mobile"`, `"desktop"`)
- `location_country`: present as `location_country` (string, nullable)
- `location_city`: present as `location_city` (string, nullable)

**Notable absences:** No separate browsing history array in this endpoint. `page_url` and `page_title` reflect the visitor's _current_ page only, and were `null` for both tested visitors (likely offline at time of request).

## GET /v2/visitors/{visitor_id}/pages

**HTTP status:** 400 Bad Request

**Response:**
```json
{"code":"not_implemented","message":"Requested url or method not implemented"}
```

This endpoint does **not exist** in the Smartsupp v2 API. Browsing history is not available via REST. Use the `page_url` field from the visitor object for current/last page, or rely on page URL from Smartsupp webhook conversation message payload.

## Summary for implementation

Field name mapping (API JSON → `SmartsuppVisitorApiResponse` C# class):

| API field        | C# property       | Notes                              |
|------------------|-------------------|------------------------------------|
| `visits`         | `Visits`          | int — total visit count            |
| `os`             | `Os`              | string, nullable                   |
| `browser`        | `Browser`         | string, nullable                   |
| `browser_version`| `BrowserVersion`  | string, nullable                   |
| `user_agent`     | `UserAgent`       | string, nullable                   |
| `platform`       | `Platform`        | string, nullable (e.g. "iPhone")   |
| `device`         | `Device`          | string, nullable ("mobile"/"desktop") |
| `location_country`| `LocationCountry` | string, nullable                  |
| `location_city`  | `LocationCity`    | string, nullable                   |
| `page_url`       | `PageUrl`         | string, nullable (current page)    |
| `page_title`     | `PageTitle`       | string, nullable                   |
| `lang`           | `Lang`            | string, nullable (ISO 639-1)       |
| `referrer`       | `Referrer`        | string, nullable                   |
| `is_online`      | `IsOnline`        | bool                               |
| `status`         | `Status`          | string ("offline"/"online")        |
| `created_at`     | `CreatedAt`       | DateTimeOffset                     |
| `updated_at`     | `UpdatedAt`       | DateTimeOffset                     |

**Pages endpoint:** does not exist — use `page_url` from the visitor object as current/last page fallback. For browsing history, Smartsupp does not expose it via REST API.
