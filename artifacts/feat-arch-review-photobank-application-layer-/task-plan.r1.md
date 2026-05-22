# Smartsupp Production Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two production bugs — spurious "customer not found" error toast for guest visitors, and double-`?` badge on agentless messages — plus remove the wasteful email-based order fallback.

**Architecture:** Backend handler stops emitting an error when a Shoptet customer is absent (returns `success=true`, `contactInfo=null`); frontend card already hides on null `contactInfo`. `AgentBadge` derives initials from `agentId` when `name` is missing, avoiding duplicate `?` symbols.

**Tech Stack:** .NET 8 / C# 12 (xUnit + FluentAssertions + Moq), React / TypeScript (Vitest + React Testing Library)

---

## File Map

| Action | Path |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoHandler.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs` |
| Modify | `frontend/src/api/hooks/useSmartsupp.ts` |
| Modify | `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx` |
| Modify | `frontend/src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx` |
| Modify | `frontend/src/components/customer-support/smartsupp/AgentBadge.tsx` |
| Modify | `frontend/src/components/customer-support/smartsupp/__tests__/AgentBadge.test.tsx` |

---

## Task 1: Make `ShoptetContactInfoDto.Customer` nullable

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs`

- [ ] **Step 1: Open the DTO file and drop `required`**

Replace the `Customer` property so it is nullable:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

public class ShoptetContactInfoDto
{
    public ShoptetCustomerSnapshotDto? Customer { get; set; }
    public List<ShoptetOrderSnapshotDto> RecentOrders { get; set; } = new();
    public DateTime? CartUpdatedAt { get; set; }
}

public class ShoptetCustomerSnapshotDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? CustomerGroup { get; set; }
    public string? PriceList { get; set; }
    public string? DefaultShippingAddress { get; set; }
}

public class ShoptetOrderSnapshotDto
{
    public required string Code { get; set; }
    public string? StatusName { get; set; }
    public decimal? TotalWithVat { get; set; }
    public string? CurrencyCode { get; set; }
    public DateTime? OrderDate { get; set; }
    public string? AdminUrl { get; set; }
}
```

Only the `ShoptetCustomerSnapshotDto? Customer` line changes (dropped `required`, added `?`). All other lines are identical to the current file.

- [ ] **Step 2: Verify the project still compiles**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 Error(s).

---

## Task 2: Rewrite backend handler tests (TDD RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs`

The handler will lose its `IEshopOrderClient` constructor parameter and its email-based order fallback. Rewrite the tests now to express the new contract; they will fail until the handler is updated in Task 3.

- [ ] **Step 1: Replace the entire test file**

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class GetSmartsuppContactShoptetInfoHandlerTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<IShoptetCustomerClient> _customerClient = new();

    private GetSmartsuppContactShoptetInfoHandler CreateHandler() =>
        new(_repo.Object, _customerClient.Object);

    private static ShoptetCustomerInfoDto MakeCustomer(string guid = "cust-1") =>
        new() { Guid = guid, FullName = "Jana Nováková", Email = "jana@test.cz", CustomerGroup = "VIP", PriceList = "Retail" };

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenConversationMissing()
    {
        _repo.Setup(r => r.GetConversationAsync("missing", It.IsAny<CancellationToken>()))
             .ReturnsAsync((SmartsuppConversation?)null);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "missing" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.SmartsuppConversationNotFound);
    }

    [Fact]
    public async Task Handle_ResolvesViaUserGuid_WhenShoptetUserGuidPresent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_user_guid":"user-guid-1","shoptet_guid":"guid-2"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("user-guid-1", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MakeCustomer("user-guid-1"));

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.Customer!.FullName.Should().Be("Jana Nováková");
        result.ContactInfo.RecentOrders.Should().BeEmpty();
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("user-guid-1", It.IsAny<CancellationToken>()), Times.Once);
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-2", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ResolvesViaShoptetGuid_WhenUserGuidAbsent()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"guid-abc"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-abc", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MakeCustomer("guid-abc"));

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        _customerClient.Verify(c => c.GetCustomerByGuidAsync("guid-abc", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CustomerNotFound_ReturnsSuccessWithNullContactInfo()
    {
        // Guest/unregistered visitor — no Shoptet customer record. Expected: success=true, ContactInfo=null (no toast, no panel).
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"unknown-guid"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("unknown-guid", It.IsAny<CancellationToken>()))
                       .ReturnsAsync((ShoptetCustomerInfoDto?)null);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoGuidsPresent_ReturnsSuccessWithNullContactInfo()
    {
        // No guid variables and no email-based lookup — email fallback is intentionally removed.
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            ContactEmail = "guest@example.com",
            VariablesJson = null,
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ParsesCartUpdatedAt_FromVariables()
    {
        var conversation = new SmartsuppConversation
        {
            Id = "c1",
            Status = SmartsuppConversationStatus.Open,
            VariablesJson = """{"shoptet_guid":"guid-1","shoptet_cart_updated_at":"2026-04-15T12:00:00"}""",
            Messages = [],
        };
        _repo.Setup(r => r.GetConversationAsync("c1", It.IsAny<CancellationToken>()))
             .ReturnsAsync(conversation);
        _customerClient.Setup(c => c.GetCustomerByGuidAsync("guid-1", It.IsAny<CancellationToken>()))
                       .ReturnsAsync(MakeCustomer("guid-1"));

        var result = await CreateHandler().Handle(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = "c1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ContactInfo!.CartUpdatedAt.Should().Be(new DateTime(2026, 4, 15, 12, 0, 0));
        result.ContactInfo.RecentOrders.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail (RED)**

```bash
dotnet test backend/test/Anela.Heblo.Tests \
  --filter FullyQualifiedName~GetSmartsuppContactShoptetInfoHandlerTests \
  --no-build 2>&1 | tail -20
```

Expected failures:
- `Handle_ResolvesViaUserGuid_WhenShoptetUserGuidPresent` — fails because old handler calls `GetRecentOrdersByEmailAsync` which is no longer set up (or fails because `CreateHandler()` passes wrong args — compile error)
- `Handle_NoGuidsPresent_ReturnsSuccessWithNullContactInfo` — fails: old handler returns `success=false` for missing customer
- `Handle_CustomerNotFound_ReturnsSuccessWithNullContactInfo` — fails: old handler returns `success=false`

> Note: The test file will not compile until Task 3 updates the handler constructor signature to remove `IEshopOrderClient`. A compile error counts as RED — proceed to Task 3.

---

## Task 3: Rewrite handler — drop email fallback and error return (TDD GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoHandler.cs`

- [ ] **Step 1: Replace the entire handler file**

```csharp
using System.Globalization;
using System.Text.Json;
using Anela.Heblo.Application.Features.ShoptetCustomers;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;

public class GetSmartsuppContactShoptetInfoHandler
    : IRequestHandler<GetSmartsuppContactShoptetInfoRequest, GetSmartsuppContactShoptetInfoResponse>
{
    private readonly ISmartsuppRepository _repo;
    private readonly IShoptetCustomerClient _customerClient;

    public GetSmartsuppContactShoptetInfoHandler(
        ISmartsuppRepository repo,
        IShoptetCustomerClient customerClient)
    {
        _repo = repo;
        _customerClient = customerClient;
    }

    public async Task<GetSmartsuppContactShoptetInfoResponse> Handle(
        GetSmartsuppContactShoptetInfoRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repo.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GetSmartsuppContactShoptetInfoResponse(ErrorCodes.SmartsuppConversationNotFound);

        var variables = ParseVariables(conversation.VariablesJson);
        variables.TryGetValue("shoptet_user_guid", out var userGuid);
        variables.TryGetValue("shoptet_guid", out var shoptetGuid);
        variables.TryGetValue("shoptet_cart_updated_at", out var cartStr);

        ShoptetCustomerInfoDto? customer = null;

        if (!string.IsNullOrWhiteSpace(userGuid))
            customer = await _customerClient.GetCustomerByGuidAsync(userGuid, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(shoptetGuid))
            customer = await _customerClient.GetCustomerByGuidAsync(shoptetGuid, cancellationToken);

        if (customer is null)
            return new GetSmartsuppContactShoptetInfoResponse { ContactInfo = null };

        DateTime? cartUpdatedAt = null;
        if (!string.IsNullOrWhiteSpace(cartStr) &&
            DateTime.TryParse(cartStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedCart))
            cartUpdatedAt = parsedCart;

        return new GetSmartsuppContactShoptetInfoResponse
        {
            ContactInfo = new ShoptetContactInfoDto
            {
                Customer = new ShoptetCustomerSnapshotDto
                {
                    FullName = customer.FullName,
                    Email = customer.Email,
                    CustomerGroup = customer.CustomerGroup,
                    PriceList = customer.PriceList,
                    DefaultShippingAddress = customer.DefaultShippingAddress,
                },
                RecentOrders = new(),
                CartUpdatedAt = cartUpdatedAt,
            },
        };
    }

    private static Dictionary<string, string> ParseVariables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}
```

Key changes from the original:
- `IEshopOrderClient _orderClient` field and constructor parameter removed
- `using Anela.Heblo.Application.Features.ShoptetOrders;` removed
- `RecentOrdersLimit` constant removed
- Email-based fallback branch (`else if (!string.IsNullOrWhiteSpace(conversation.ContactEmail))`) removed
- `if (customer is null)` now returns `success=true` with `ContactInfo = null` instead of emitting `SmartsuppShoptetCustomerNotFound`
- `GetRecentOrdersByEmailAsync`, `GetOrderStatusNamesAsync` calls removed
- `RecentOrders = new()` (always empty until a future efficient order-lookup path)

> `IEshopOrderClient` itself is NOT removed — it is used by other features. Only the injection into this handler is dropped.

- [ ] **Step 2: Check whether the DI registration needs updating**

Search for where `GetSmartsuppContactShoptetInfoHandler` is registered:

```bash
grep -r "GetSmartsuppContactShoptetInfoHandler\|AddSmartsuppServices\|AddApplicationServices" \
  backend/src --include="*.cs" -l
```

If a DI registration explicitly passes `IEshopOrderClient` as a constructor arg, update it. More likely it is auto-registered via MediatR assembly scanning — in that case no change needed. Confirm by reading the found files.

- [ ] **Step 3: Run backend tests (GREEN)**

```bash
dotnet test backend/test/Anela.Heblo.Tests \
  --filter FullyQualifiedName~GetSmartsuppContactShoptetInfoHandlerTests
```

Expected: All 6 tests pass, 0 failures.

- [ ] **Step 4: Full backend build + format**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: Build succeeded, no format violations. If format reports changes, run `dotnet format backend/Anela.Heblo.sln` (without `--verify-no-changes`) to apply them, then re-run to verify.

- [ ] **Step 5: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ShoptetContactInfoDto.cs \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetContactShoptetInfo/GetSmartsuppContactShoptetInfoHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Smartsupp/GetSmartsuppContactShoptetInfoHandlerTests.cs
git commit -m "fix(smartsupp): guest customer returns success with null ContactInfo, drop email order fallback"
```

---

## Task 4: Update frontend TypeScript type for nullable customer

**Files:**
- Modify: `frontend/src/api/hooks/useSmartsupp.ts`

- [ ] **Step 1: Make `customer` nullable in `ShoptetContactInfoDto`**

In `useSmartsupp.ts`, locate `ShoptetContactInfoDto` (currently around line 137–141) and change `customer`:

```ts
export interface ShoptetContactInfoDto {
  customer?: ShoptetCustomerSnapshotDto | null;
  recentOrders: ShoptetOrderSnapshotDto[];
  cartUpdatedAt?: string | null;
}
```

Only the `customer` line changes — from `customer: ShoptetCustomerSnapshotDto;` to `customer?: ShoptetCustomerSnapshotDto | null;`.

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd frontend && npx tsc --noEmit 2>&1 | head -30
```

Expected: zero errors (or only pre-existing errors unrelated to this change).

---

## Task 5: Write new `ShoptetCustomerCard` test (TDD RED)

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx`

- [ ] **Step 1: Add a test for null customer within a non-null contactInfo**

Add after the existing `describe("ShoptetCustomerCard")` block's last test (before the closing `}`):

```tsx
  it("renders no customer section when contactInfo.customer is null", () => {
    mockedHook.mockReturnValue({
      data: { success: true, contactInfo: { customer: null, recentOrders: [], cartUpdatedAt: null } },
      isLoading: false,
    });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.queryByText("Shoptet Zákazník")).not.toBeInTheDocument();
  });

  it("still renders cart section when customer is null but cartUpdatedAt is set", () => {
    mockedHook.mockReturnValue({
      data: {
        success: true,
        contactInfo: { customer: null, recentOrders: [], cartUpdatedAt: "2026-04-15T12:00:00" },
      },
      isLoading: false,
    });
    render(<ShoptetCustomerCard conversationId="c1" />, { wrapper });
    expect(screen.queryByText("Shoptet Zákazník")).not.toBeInTheDocument();
    expect(screen.getByText("Shoptet Košík")).toBeInTheDocument();
  });
```

- [ ] **Step 2: Run the new tests to confirm RED**

```bash
cd frontend && npx vitest run \
  src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx \
  2>&1 | tail -20
```

Expected: The two new tests fail (one with a TypeError because `customer.fullName` crashes on null, one for the same reason). Existing tests still pass.

---

## Task 6: Fix `ShoptetCustomerCard.tsx` to guard on nullable customer (TDD GREEN)

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx`

- [ ] **Step 1: Replace the component body**

```tsx
import React from "react";
import { useSmartsuppShoptetInfo } from "../../../api/hooks/useSmartsupp";
import Section from "./Section";

interface ShoptetCustomerCardProps {
  conversationId: string | null;
}

function ShoptetCustomerCard({ conversationId }: ShoptetCustomerCardProps) {
  const { data, isLoading } = useSmartsuppShoptetInfo(conversationId);

  if (isLoading) return null;
  if (!data?.contactInfo) return null;

  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  const hasCustomer = customer != null;
  const hasOrders = recentOrders.length > 0;
  const hasCart = !!cartUpdatedAt;

  if (!hasCustomer && !hasOrders && !hasCart) return null;

  return (
    <>
      {hasCustomer && (
        <Section title="Shoptet Zákazník">
          <div className="space-y-1">
            {customer.fullName && (
              <div className="text-sm font-semibold text-gray-900">{customer.fullName}</div>
            )}
            {customer.email && (
              <div className="text-xs text-gray-500">{customer.email}</div>
            )}
            {customer.customerGroup && (
              <div className="text-xs text-gray-700">
                <span className="text-gray-400">Skupina: </span>{customer.customerGroup}
              </div>
            )}
            {customer.priceList && (
              <div className="text-xs text-gray-700">
                <span className="text-gray-400">Ceník: </span>{customer.priceList}
              </div>
            )}
            {customer.defaultShippingAddress && (
              <div className="text-xs text-gray-600 mt-1">{customer.defaultShippingAddress}</div>
            )}
          </div>
        </Section>
      )}

      {hasCart && (
        <Section title="Shoptet Košík">
          <div className="text-xs text-gray-500">
            Aktualizován: {new Date(cartUpdatedAt!).toLocaleDateString("cs-CZ")}
          </div>
        </Section>
      )}

      {hasOrders && (
        <Section title="Poslední objednávky">
          <div className="space-y-2">
            {recentOrders.map((order) => (
              <div key={order.code} className="border-b border-gray-50 pb-1.5 last:border-0">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-gray-800">{order.code}</span>
                  {order.totalWithVat != null && (
                    <span className="text-xs text-gray-700">
                      {order.totalWithVat.toLocaleString("cs-CZ")} {order.currencyCode ?? "Kč"}
                    </span>
                  )}
                </div>
                <div className="flex items-center justify-between mt-0.5">
                  {order.statusName && (
                    <span className="text-[11px] text-gray-500">{order.statusName}</span>
                  )}
                  {order.orderDate && (
                    <span className="text-[11px] text-gray-400">
                      {new Date(order.orderDate).toLocaleDateString("cs-CZ")}
                    </span>
                  )}
                </div>
                {order.adminUrl && (
                  <a
                    href={order.adminUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-[11px] text-blue-600 hover:underline"
                  >
                    Zobrazit v Shoptet
                  </a>
                )}
              </div>
            ))}
          </div>
        </Section>
      )}
    </>
  );
}

export default ShoptetCustomerCard;
```

Changes from original: wrapped the customer `<Section>` in `{hasCustomer && ...}`, replaced bare `{cartUpdatedAt && ...}` / `{recentOrders.length > 0 && ...}` with `hasCart` / `hasOrders` booleans, added early return when all three sections are empty.

- [ ] **Step 2: Run all ShoptetCustomerCard tests (GREEN)**

```bash
cd frontend && npx vitest run \
  src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx \
  2>&1 | tail -20
```

Expected: All 7 tests pass (5 existing + 2 new), 0 failures.

- [ ] **Step 3: Commit**

```bash
cd ..  # back to repo root
git add \
  frontend/src/api/hooks/useSmartsupp.ts \
  frontend/src/components/customer-support/smartsupp/ShoptetCustomerCard.tsx \
  frontend/src/components/customer-support/smartsupp/__tests__/ShoptetCustomerCard.test.tsx
git commit -m "fix(smartsupp): silently hide Shoptet panel when customer is absent, guard nullable customer"
```

---

## Task 7: Rewrite `AgentBadge` tests (TDD RED)

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/__tests__/AgentBadge.test.tsx`

- [ ] **Step 1: Replace the test file**

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import AgentBadge from "../AgentBadge";

describe("AgentBadge", () => {
  it("renders the agent name as the label", () => {
    render(<AgentBadge agentId="a1" name="Petr Novák" />);
    expect(screen.getByText("Petr Novák")).toBeInTheDocument();
  });

  it("renders initials from the name when name is provided", () => {
    render(<AgentBadge agentId="a1" name="Petr Novák" />);
    expect(screen.getByText("PN")).toBeInTheDocument();
  });

  it("renders agent label and agentId-derived initials when name is null", () => {
    render(<AgentBadge agentId="a1" name={null} />);
    expect(screen.getByText("Agent")).toBeInTheDocument();
    expect(screen.getByText("A1")).toBeInTheDocument();
    expect(screen.queryByText("?")).not.toBeInTheDocument();
  });

  it("renders ? initials and Agent label when both name and agentId are absent", () => {
    render(<AgentBadge agentId={null} name={null} />);
    expect(screen.getByText("?")).toBeInTheDocument();
    expect(screen.getByText("Agent")).toBeInTheDocument();
  });

  it("uses the same color for the same agentId across renders", () => {
    const { rerender } = render(<AgentBadge agentId="a1" name="Petr" />);
    const first = screen.getByTestId("agent-badge").className;
    rerender(<AgentBadge agentId="a1" name="Petr" />);
    const second = screen.getByTestId("agent-badge").className;
    expect(first).toBe(second);
  });

  it("renders different colors for different agentIds (sanity)", () => {
    const { rerender } = render(<AgentBadge agentId="aaa" name="A" />);
    const colorA = screen.getByTestId("agent-badge").className;
    rerender(<AgentBadge agentId="zzz" name="Z" />);
    const colorZ = screen.getByTestId("agent-badge").className;
    expect(colorA).not.toBe(colorZ);
  });
});
```

Key changes:
- `"renders the initials when no name is provided"` → replaced by two tests: one for `name=null, agentId set` and one for both absent
- The double-`?` test (`getAllByText("?").length >= 1`) is gone; the new test asserts no `?` appears when `agentId` is set

- [ ] **Step 2: Run the tests to confirm RED**

```bash
cd frontend && npx vitest run \
  src/components/customer-support/smartsupp/__tests__/AgentBadge.test.tsx \
  2>&1 | tail -20
```

Expected: `"renders agent label and agentId-derived initials when name is null"` fails (current code renders `?` twice), `"renders ? initials and Agent label when both name and agentId are absent"` fails (current code renders `?` as label too).

---

## Task 8: Fix `AgentBadge.tsx` — eliminate double `?` (TDD GREEN)

**Files:**
- Modify: `frontend/src/components/customer-support/smartsupp/AgentBadge.tsx`

- [ ] **Step 1: Replace the component file**

```tsx
import React from "react";
import { getAgentColor } from "./utils/agentColor";

interface AgentBadgeProps {
  agentId?: string | null;
  name?: string | null;
  showInitials?: boolean;
}

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/);
  return parts.length >= 2
    ? `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase()
    : name.slice(0, 2).toUpperCase();
}

const AgentBadge: React.FC<AgentBadgeProps> = ({ agentId, name, showInitials = true }) => {
  const color = getAgentColor(agentId);
  const trimmedName = name?.trim();
  const hasName = !!trimmedName;
  const initials = hasName
    ? getInitials(trimmedName!)
    : agentId
        ? agentId.slice(0, 2).toUpperCase()
        : "?";
  const label = hasName ? trimmedName! : "Agent";

  return (
    <span
      data-testid="agent-badge"
      className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${color.bg} ${color.text}`}
    >
      {showInitials && (
        <span className={`inline-flex items-center justify-center w-4 h-4 rounded-full ring-1 ${color.ring} bg-white text-[10px] font-semibold`}>
          {initials}
        </span>
      )}
      <span className="truncate max-w-[10rem]">{label}</span>
    </span>
  );
};

export default AgentBadge;
```

Changes from original:
- `getInitials` now accepts `string` (not `string | null | undefined`) — caller is responsible for the null check
- `trimmedName` / `hasName` variables introduced
- `initials` derived from `agentId` when name absent
- `label` is `"Agent"` when name absent (not `initials`, so never a `?` label)

- [ ] **Step 2: Run AgentBadge tests (GREEN)**

```bash
cd frontend && npx vitest run \
  src/components/customer-support/smartsupp/__tests__/AgentBadge.test.tsx \
  2>&1 | tail -20
```

Expected: All 6 tests pass, 0 failures.

- [ ] **Step 3: Run the full frontend test suite to catch any snapshot or snapshot-like regressions**

```bash
cd frontend && npx vitest run 2>&1 | tail -30
```

Expected: All tests pass (or only pre-existing failures). If any other test breaks because it asserts `"?"` text from `AgentBadge`, update that assertion to `"Agent"` / the agentId-derived initials.

- [ ] **Step 4: Commit**

```bash
cd ..
git add \
  frontend/src/components/customer-support/smartsupp/AgentBadge.tsx \
  frontend/src/components/customer-support/smartsupp/__tests__/AgentBadge.test.tsx
git commit -m "fix(smartsupp): derive initials from agentId when name is absent, show Agent label instead of double ?"
```

---

## Task 9: Final build gates

- [ ] **Step 1: Frontend build + lint**

```bash
cd frontend && npm run build 2>&1 | tail -20
npm run lint 2>&1 | tail -20
```

Expected: Build succeeds, no lint errors.

- [ ] **Step 2: Full backend build**

```bash
cd .. && dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 3: Run all backend Smartsupp tests one final time**

```bash
dotnet test backend/test/Anela.Heblo.Tests \
  --filter FullyQualifiedName~Smartsupp
```

Expected: All pass.

---

## Verification Checklist (manual, against staging)

After deploying:

- [ ] Open a conversation for a guest/unregistered visitor (no Shoptet customer profile). No toast appears. The Shoptet block on the right side is absent.
- [ ] Open a conversation for a registered Shoptet customer. "Shoptet Zákazník" section appears. "Poslední objednávky" is empty (expected until a future order-lookup path lands).
- [ ] Open a conversation whose outgoing reply has `authorName = null` but `agentId` set. Badge shows 2-letter initials from the agentId and label `"Agent"`. No two `?` side-by-side.
- [ ] Watch the browser network panel: no `GET /api/orders?page=1&itemsPerPage=…` calls fire when opening conversations.
