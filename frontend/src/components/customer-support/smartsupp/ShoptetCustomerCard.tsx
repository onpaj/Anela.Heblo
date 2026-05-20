import React from "react";
import { useSmartsuppShoptetInfo } from "../../../api/hooks/useSmartsupp";

interface ShoptetCustomerCardProps {
  conversationId: string | null;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="px-4 py-3 border-b border-gray-100">
      <div className="text-[11px] uppercase tracking-wide text-gray-400 font-medium mb-1.5">
        {title}
      </div>
      {children}
    </div>
  );
}

function ShoptetCustomerCard({ conversationId }: ShoptetCustomerCardProps) {
  const { data, isLoading } = useSmartsuppShoptetInfo(conversationId);

  if (isLoading || !data?.contactInfo) return null;

  const { customer, recentOrders, cartUpdatedAt } = data.contactInfo;

  return (
    <>
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

      {cartUpdatedAt && (
        <Section title="Shoptet Košík">
          <div className="text-xs text-gray-500">
            Aktualizován: {new Date(cartUpdatedAt).toLocaleDateString("cs-CZ")}
          </div>
        </Section>
      )}

      {recentOrders.length > 0 && (
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
