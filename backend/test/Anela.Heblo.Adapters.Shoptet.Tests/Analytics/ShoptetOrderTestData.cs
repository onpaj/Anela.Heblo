using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

/// <summary>
/// Order payloads shaped exactly like the live GET /api/orders/{code} responses observed on
/// anela.cz on 2026-09-22 — including the quirks: prices as JSON strings, amounts as JSON numbers,
/// the "+0200" basic-format offset, and product-set components living only in completion[].
/// Customer data is invented; the structure is not.
/// </summary>
internal static class ShoptetOrderTestData
{
    public const string SimpleOrderJson = """
    {
      "data": {
        "order": {
          "code": "126020373",
          "guid": "01a0c541-3e64-72b6-aa40-1be99e651226",
          "externalCode": null,
          "customerGuid": null,
          "email": "  Buyer@Example.COM ",
          "creationTime": "2026-09-21T20:36:22+0200",
          "changeTime": "2026-09-22T12:14:17+0200",
          "cashDeskOrder": false,
          "salesChannelGuid": "0199be14-cf30-7071-8002-7ac7a4338548",
          "stockId": 1,
          "vatPayer": true,
          "vatMode": "Normal",
          "paid": true,
          "language": "cs",
          "referer": null,
          "billingAddress": {
            "company": null, "companyId": null,
            "city": "Brno", "zip": "60200", "countryCode": "CZ"
          },
          "price": {
            "vat": "104.15", "toPay": "600.00", "currencyCode": "CZK",
            "withVat": "600.00", "withoutVat": "495.85", "exchangeRate": "1.00000000"
          },
          "source": { "id": -1, "name": "E-shop" },
          "status": { "id": 70, "name": "Předáno přepravci" },
          "billingMethod": { "name": "Dobírkou", "id": 1 },
          "paymentMethod": { "guid": "d538520e-e920-11e0-baa3-7dc668b75ca8", "name": "Dobírkou" },
          "shipping": { "guid": "68201aa2-6343-11f1-9239-bc241122355e", "name": "Zásilkovna boxy a výdejní místa" },
          "items": [
            {
              "productGuid": "4796401e-88c2-11ea-b7db-0cc47a6b4bcc",
              "itemType": "product", "productType": "product",
              "name": "Odlíčím", "variantName": "Obsah: 100 ml", "brand": "Anela", "ean": null,
              "weight": "0.194", "amount": 2.000, "amountUnit": "ks", "code": "ODL001100",
              "itemId": 1551991,
              "itemPrice":  { "withVat": "700.00", "withoutVat": "578.52", "vat": "121.48", "vatRate": "21.00" },
              "unitPrice":  { "withVat": "350.00", "withoutVat": "289.26", "vat": "60.74", "vatRate": "21.00" },
              "purchasePrice": { "withVat": "106.68", "withoutVat": "88.17", "vat": "18.51", "vatRate": "21.00" }
            },
            {
              "itemType": "shipping", "productType": "shipping",
              "name": "Zásilkovna boxy a výdejní místa", "amount": 1.000, "code": null,
              "itemId": 1552006,
              "itemPrice": { "withVat": "69.00", "withoutVat": "57.02", "vat": "11.98", "vatRate": "21.00" },
              "unitPrice": { "withVat": "69.00", "withoutVat": "57.02", "vat": "11.98", "vatRate": "21.00" }
            },
            {
              "itemType": "billing", "productType": "billing",
              "name": "Dobírkou", "amount": 1.000, "code": null,
              "itemId": 1552009,
              "itemPrice": { "withVat": "39.00", "withoutVat": "32.23", "vat": "6.77", "vatRate": "21.00" },
              "unitPrice": { "withVat": "39.00", "withoutVat": "32.23", "vat": "6.77", "vatRate": "21.00" }
            },
            {
              "itemType": "discount-coupon", "productType": "discount-coupon",
              "name": "Slevový kupon - Sleva 208 Kč", "amount": 1.000, "code": null,
              "itemId": 1552012,
              "itemPrice": { "withVat": "-208.00", "withoutVat": "-171.90", "vat": "-36.10", "vatRate": "21.00" },
              "unitPrice": { "withVat": "-208.00", "withoutVat": "-171.90", "vat": "-36.10", "vatRate": "21.00" }
            }
          ],
          "completion": [
            {
              "productGuid": "4796401e-88c2-11ea-b7db-0cc47a6b4bcc",
              "itemType": "product", "name": "Odlíčím", "variantName": "Obsah: 100 ml",
              "amount": 2.000, "amountUnit": "ks", "code": "ODL001100", "itemId": 1551991
            }
          ]
        }
      }
    }
    """;

    /// <summary>
    /// The real shape of order 126014786: two of set SA010, whose four components appear only in
    /// completion[] with amount already multiplied out (2 sets × 1 each = 2) and with no price.
    /// </summary>
    public const string ProductSetOrderJson = """
    {
      "data": {
        "order": {
          "code": "126014786",
          "guid": "0190a1b2-c3d4-7000-8000-000000000001",
          "customerGuid": "54e236e2-2e7b-11f0-8744-5a322b0c1836",
          "email": "sets@example.com",
          "creationTime": "2026-06-11T09:15:00+0200",
          "changeTime": "2026-06-12T09:15:00+0200",
          "cashDeskOrder": false,
          "salesChannelGuid": "0199be14-cf30-7071-8002-7ac7a4338548",
          "vatPayer": true,
          "paid": true,
          "price": {
            "vat": "410.45", "toPay": "2365.00", "currencyCode": "CZK",
            "withVat": "2365.00", "withoutVat": "1954.55", "exchangeRate": "1.00000000"
          },
          "source": { "id": -1, "name": "E-shop" },
          "status": { "id": -3, "name": "Vyřízena" },
          "shipping": { "guid": "6fc70492-6341-11f1-9239-bc241122355e", "name": "PPL přímo do PPL boxu" },
          "items": [
            {
              "itemType": "product", "productType": "product",
              "name": "Lesa pán", "code": "SEZ001030", "amount": 1.000, "itemId": 1475188,
              "itemPrice": { "withVat": "150.00", "withoutVat": "123.97", "vat": "26.03", "vatRate": "21.00" },
              "unitPrice": { "withVat": "150.00", "withoutVat": "123.97", "vat": "26.03", "vatRate": "21.00" }
            },
            {
              "itemType": "product-set", "productType": "product-set",
              "name": "Set pro citlivou pleť", "code": "SA010", "amount": 2.000, "itemId": 1475182,
              "itemPrice": { "withVat": "1780.00", "withoutVat": "1471.07", "vat": "308.93", "vatRate": "21.00" },
              "unitPrice": { "withVat": "890.00", "withoutVat": "735.54", "vat": "154.46", "vatRate": "21.00" }
            },
            {
              "itemType": "shipping", "productType": "shipping",
              "name": "PPL přímo do PPL boxu", "amount": 1.000, "code": null, "itemId": 1475194,
              "itemPrice": { "withVat": "0.00", "withoutVat": "0.00", "vat": "0.00", "vatRate": "21.00" },
              "unitPrice": { "withVat": "0.00", "withoutVat": "0.00", "vat": "0.00", "vatRate": "21.00" }
            }
          ],
          "completion": [
            {
              "itemType": "product", "name": "Lesa pán", "code": "SEZ001030",
              "amount": 1.000, "amountUnit": "ks", "itemId": 1475188
            },
            {
              "itemType": "product-set", "name": "Set pro citlivou pleť", "code": "SA010",
              "amount": 2.000, "amountUnit": "ks", "itemId": 1475182
            },
            {
              "itemType": "product-set-item", "code": "SER004005", "name": "Růžové z nebe",
              "variantName": "Obsah: 5 ml vzorek", "amount": 2.000, "amountUnit": "ks",
              "itemId": 710, "parentProductSetItemId": 1475182
            },
            {
              "itemType": "product-set-item", "code": "SER003005", "name": "Bezstarostný motýl",
              "variantName": "Obsah: 5 ml vzorek", "amount": 2.000, "amountUnit": "ks",
              "itemId": 1313, "parentProductSetItemId": 1475182
            }
          ]
        }
      }
    }
    """;

    /// <summary>A cash-desk (prodejna) order: no e-mail, no customer, no shipping, "paid": null.</summary>
    public const string CashDeskOrderJson = """
    {
      "data": {
        "order": {
          "code": "126020422",
          "guid": "01a0c905-eb8e-73ad-aa64-aefc89e9c7dd",
          "customerGuid": null,
          "email": null,
          "creationTime": "2026-09-22T14:10:03+0200",
          "changeTime": "2026-09-22T14:10:03+0200",
          "cashDeskOrder": true,
          "salesChannelGuid": "019eab6a-7f98-716f-889c-f99d15219194",
          "vatPayer": null,
          "paid": null,
          "price": {
            "vat": "173.55", "toPay": "1000.00", "currencyCode": "CZK",
            "withVat": "1000.00", "withoutVat": "826.45", "exchangeRate": "1.00000000"
          },
          "source": { "id": 1000, "name": "Sdilena" },
          "status": { "id": -3, "name": "Vyřízena" },
          "shipping": null,
          "paymentMethod": null,
          "items": [
            {
              "itemType": "product", "productType": "product",
              "name": "Pultový prodej", "code": "PRO001", "amount": 1.000, "itemId": 1600001,
              "itemPrice": { "withVat": "1000.00", "withoutVat": "826.45", "vat": "173.55", "vatRate": "21.00" },
              "unitPrice": { "withVat": "1000.00", "withoutVat": "826.45", "vat": "173.55", "vatRate": "21.00" }
            }
          ],
          "completion": []
        }
      }
    }
    """;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ShoptetOrderDetailDto Parse(string json) =>
        JsonSerializer.Deserialize<ShoptetOrderDetailResponse>(json, Options)!.Data!.Order!;
}
