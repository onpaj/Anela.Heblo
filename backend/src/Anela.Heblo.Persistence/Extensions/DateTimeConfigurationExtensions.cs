using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;

namespace Anela.Heblo.Persistence.Extensions
{
    /// <summary>
    /// Extensions for standardizing DateTime column configurations across all entities.
    /// 
    /// STANDARD: All DateTime properties use 'timestamp' (without time zone) in PostgreSQL
    /// and store UTC values in the application layer.
    /// </summary>
    public static class DateTimeConfigurationExtensions
    {
        /// <summary>
        /// Configures a DateTime property to use PostgreSQL 'timestamp' type (without time zone).
        /// This ensures consistent handling across all entities and avoids PostgreSQL timezone conflicts.
        /// 
        /// APPLICATION RULE: Always store UTC values using DateTime.UtcNow in business logic.
        /// </summary>
        /// <param name="propertyBuilder">The property builder</param>
        /// <returns>The property builder for chaining</returns>
        public static PropertyBuilder<DateTime> AsUtcTimestamp(
            this PropertyBuilder<DateTime> propertyBuilder)
        {
            return propertyBuilder.HasColumnType("timestamp");
        }

        /// <summary>
        /// Configures a nullable DateTime property to use PostgreSQL 'timestamp' type (without time zone).
        /// This ensures consistent handling across all entities and avoids PostgreSQL timezone conflicts.
        /// 
        /// APPLICATION RULE: Always store UTC values using DateTime.UtcNow in business logic.
        /// </summary>
        /// <param name="propertyBuilder">The property builder</param>
        /// <returns>The property builder for chaining</returns>
        public static PropertyBuilder<DateTime?> AsUtcTimestamp(
            this PropertyBuilder<DateTime?> propertyBuilder)
        {
            return propertyBuilder.HasColumnType("timestamp");
        }
    }
}