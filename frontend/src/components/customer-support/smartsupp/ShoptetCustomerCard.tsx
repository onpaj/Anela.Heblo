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

  if (!hasCustomer && !hasOrders && !cartUpdatedAt) return null;

  return (
    <>
      {hasCustomer && (
        <Section title="Shoptet Zákazník">
          <div className="space-y-1">
            {customer.fullName && (
              <div className="text-sm font-semibold text-gray-900 dark:text-graphite-text">{customer.fullName}</div>
            )}
            {customer.email && (
              <div className="text-xs text-gray-500 dark:text-graphite-muted">{customer.email}</div>
            )}
            {customer.customerGroup && (
              <div className="text-xs text-gray-700 dark:text-graphite-muted">
                <span className="text-gray-400 dark:text-graphite-faint">Skupina: </span>{customer.customerGroup}
              </div>
            )}
            {customer.priceList && (
              <div className="text-xs text-gray-700 dark:text-graphite-muted">
                <span className="text-gray-400 dark:text-graphite-faint">Ceník: </span>{customer.priceList}
              </div>
            )}
            {customer.defaultShippingAddress && (
              <div className="text-xs text-gray-600 dark:text-graphite-muted mt-1">{customer.defaultShippingAddress}</div>
            )}
          </div>
        </Section>
      )}

      {cartUpdatedAt != null && (
        <Section title="Shoptet Košík">
          <div className="text-xs text-gray-500 dark:text-graphite-muted">
            Aktualizován: {new Date(cartUpdatedAt).toLocaleDateString("cs-CZ")}
          </div>
        </Section>
      )}

      {hasOrders && (
        <Section title="Poslední objednávky">
          <div className="space-y-2">
            {recentOrders.map((order) => (
              <div key={order.code} className="border-b border-gray-50 dark:border-graphite-border pb-1.5 last:border-0">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-medium text-gray-800 dark:text-graphite-text">{order.code}</span>
                  {order.totalWithVat != null && (
                    <span className="text-xs text-gray-700 dark:text-graphite-muted">
                      {order.totalWithVat.toLocaleString("cs-CZ")} {order.currencyCode ?? "Kč"}
                    </span>
                  )}
                </div>
                <div className="flex items-center justify-between mt-0.5">
                  {order.statusName && (
                    <span className="text-[11px] text-gray-500 dark:text-graphite-muted">{order.statusName}</span>
                  )}
                  {order.orderDate && (
                    <span className="text-[11px] text-gray-400 dark:text-graphite-faint">
                      {new Date(order.orderDate).toLocaleDateString("cs-CZ")}
                    </span>
                  )}
                </div>
                {order.adminUrl && (
                  <a
                    href={order.adminUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-[11px] text-blue-600 dark:text-blue-400 hover:underline"
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
