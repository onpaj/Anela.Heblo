using static Anela.Heblo.Application.Features.Reporting.Catalog.ReportColumn;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public static partial class ReportDatasetCatalog
{
    private const string ShoptetSchema = "shoptet_raw";
    private const string OrderMonthDesc = "Order month (first day, Europe/Prague).";
    private const string ChannelDesc = "Sales channel: eshop_mo (retail e-shop), eshop_vo (wholesale e-shop), prodejna (in-store cash desk).";
    private const string CustomerStatusDesc = "new = first ever non-cancelled order; returning = previous order within 12 months; returning_lapsed = previous order more than 12 months ago; unknown = no identity (cash desk).";
    private const string RevenueCaveat = "Amounts are CZK (foreign-currency orders converted). *_with_vat includes VAT. Cancelled orders are excluded.";
    private const string OrderedNotShipped = "Counts are pieces/orders ORDERED (order date), not packed or shipped.";

    private static IEnumerable<ReportDataset> Eshop()
    {
        yield return new ReportDataset
        {
            Id = "shoptet.orders_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_order_monthly",
            Description = "Orders, customers, revenue and units per month and sales channel — the company's real order numbers.",
            Grain = "month × channel",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Int("orders", "Non-cancelled orders."),
                Int("customers", "Distinct customers."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK (from the order header, net of discounts)."),
                Dec("revenue_without_vat_czk", "Order revenue excl. VAT, CZK."),
                Dec("product_revenue_with_vat_czk", "Revenue from products only, incl. VAT, CZK."),
                Dec("shipping_charged_with_vat_czk", "Shipping charged to customers, incl. VAT, CZK."),
                Dec("payment_fee_with_vat_czk", "Payment-method fees charged, incl. VAT, CZK."),
                Dec("discount_with_vat_czk", "Discounts given, incl. VAT, CZK."),
                Dec("units", "Product pieces ordered."),
                Dec("avg_order_value_with_vat_czk", "Average order value incl. VAT, CZK."),
                Dec("avg_basket_units", "Average pieces per order."),
            ],
            KeyColumns = ["order_month", "channel"],
            DefaultSort = [("order_month", true), ("channel", false)],
            Caveats = [RevenueCaveat, OrderedNotShipped, "History starts 2018-09."],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.orders_by_shipping_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_order_shipping_monthly",
            Description = "Orders and shipping charged per shipping method, month and channel.",
            Grain = "month × channel × shipping method",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("shipping_name", "Shipping method name."),
                Text("shipping_guid", "Shoptet shipping method id."),
                Int("orders", "Non-cancelled orders."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK."),
                Dec("shipping_charged_with_vat_czk", "Shipping charged to customers, incl. VAT, CZK."),
                Dec("avg_shipping_charged_with_vat_czk", "Average shipping charged per order, CZK."),
                Int("orders_with_free_shipping", "Orders with zero shipping charge."),
            ],
            KeyColumns = ["order_month", "channel", "shipping_name", "shipping_guid"],
            DefaultSort = [("order_month", true), ("orders", true)],
            Caveats = [RevenueCaveat],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.orders_by_payment_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_order_payment_monthly",
            Description = "Orders and payment fees per payment method, month and channel.",
            Grain = "month × channel × payment method",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("payment_method_name", "Payment method name."),
                Int("orders", "Non-cancelled orders."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK."),
                Dec("avg_order_value_with_vat_czk", "Average order value incl. VAT, CZK."),
                Dec("payment_fee_with_vat_czk", "Payment-method fees charged, incl. VAT, CZK."),
            ],
            KeyColumns = ["order_month", "channel", "payment_method_name"],
            DefaultSort = [("order_month", true), ("orders", true)],
            Caveats = [RevenueCaveat],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.customer_types_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_customer_type_monthly",
            Description = "Orders, customers and revenue split into new vs returning customers per month and channel.",
            Grain = "month × channel × customer status",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("customer_status", CustomerStatusDesc),
                Int("orders", "Non-cancelled orders."),
                Int("customers", "Distinct customers."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK."),
                Dec("avg_order_value_with_vat_czk", "Average order value incl. VAT, CZK."),
                Dec("units", "Product pieces ordered."),
                Dec("avg_basket_units", "Average pieces per order."),
            ],
            KeyColumns = ["order_month", "channel", "customer_status"],
            DefaultSort = [("order_month", true), ("channel", false), ("customer_status", false)],
            Caveats = [RevenueCaveat, "A customer is identified by e-mail (guest checkouts included)."],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.new_customers_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_new_customers_monthly",
            Description = "Newly acquired customers (first ever order) per month and channel, with first-order value.",
            Grain = "month × channel",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Int("new_customers", "Customers whose first ever non-cancelled order fell in this month."),
                Dec("first_order_revenue_with_vat_czk", "Revenue of those first orders, incl. VAT, CZK."),
                Dec("avg_first_order_value_with_vat_czk", "Average first-order value incl. VAT, CZK."),
                Dec("avg_first_order_units", "Average pieces in a first order."),
            ],
            KeyColumns = ["order_month", "channel"],
            DefaultSort = [("order_month", true), ("channel", false)],
            Caveats = [RevenueCaveat, "A customer is identified by e-mail (guest checkouts included)."],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.product_sales_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_product_sales_monthly",
            Description = "Units and revenue per product variant, month and channel. Filter by product_code or product_name.",
            Grain = "month × channel × product variant",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("product_code", "Product code (e.g. SER004030)."),
                Text("product_name", "Product name."),
                Text("variant_name", "Variant name (size etc.), may be empty."),
                Text("item_type", "product or product-set."),
                Int("orders", "Orders containing the item."),
                Dec("units", "Pieces ordered."),
                Dec("revenue_with_vat_czk", "Item revenue incl. VAT, CZK."),
                Dec("revenue_without_vat_czk", "Item revenue excl. VAT, CZK."),
            ],
            KeyColumns = ["order_month", "channel", "product_code", "variant_name", "item_type"],
            DefaultSort = [("order_month", true), ("revenue_with_vat_czk", true)],
            Caveats =
            [
                RevenueCaveat,
                OrderedNotShipped,
                "Sets are counted as the set (item_type product-set); for the pieces inside sets use shoptet.set_component_units_monthly.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.product_sales_by_customer_type_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_product_sales_by_customer_type_monthly",
            Description = "Units and revenue per product variant split by new vs returning customers, per month and channel.",
            Grain = "month × channel × customer status × product variant",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("customer_status", CustomerStatusDesc),
                Text("product_code", "Product code."),
                Text("product_name", "Product name."),
                Text("variant_name", "Variant name, may be empty."),
                Int("orders", "Orders containing the item."),
                Dec("units", "Pieces ordered."),
                Dec("revenue_with_vat_czk", "Item revenue incl. VAT, CZK."),
            ],
            KeyColumns = ["order_month", "channel", "customer_status", "product_code", "variant_name"],
            DefaultSort = [("order_month", true), ("revenue_with_vat_czk", true)],
            Caveats = [RevenueCaveat, OrderedNotShipped],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.set_component_units_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_product_set_component_units_monthly",
            Description = "Pieces of each product sold inside product sets, per set, month and channel.",
            Grain = "month × channel × component product × set",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("product_code", "Component product code."),
                Text("product_name", "Component product name."),
                Text("variant_name", "Component variant name, may be empty."),
                Text("set_code", "Code of the set the component was sold in."),
                Int("orders", "Orders containing the set."),
                Dec("units", "Component pieces ordered via the set."),
            ],
            KeyColumns = ["order_month", "channel", "product_code", "variant_name", "set_code"],
            DefaultSort = [("order_month", true), ("units", true)],
            Caveats = [OrderedNotShipped, "Cancelled orders are excluded."],
        };
    }
}
