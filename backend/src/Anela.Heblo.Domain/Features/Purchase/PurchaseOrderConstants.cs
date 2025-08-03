namespace Anela.Heblo.Domain.Features.Purchase;

public static class PurchaseOrderConstants
{
    public const int OrderNumberMaxLength = 50;
    public const int SupplierNameMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public const int ActionMaxLength = 200;
    public const int ValueMaxLength = 500;
    public const int UserNameMaxLength = 100;

    public static readonly string[] ValidStatusTransitions = new[]
    {
        "Draft -> InTransit",
        "InTransit -> Completed"
    };

    public static class ValidationMessages
    {
        public const string OrderNumberRequired = "Order number is required";
        public const string OrderNumberTooLong = "Order number cannot exceed 50 characters";
        public const string SupplierIdRequired = "Supplier ID is required";
        public const string OrderDateRequired = "Order date is required";
        public const string CreatedByRequired = "Created by is required";
        public const string QuantityMustBePositive = "Quantity must be greater than zero";
        public const string UnitPriceCannotBeNegative = "Unit price cannot be negative";
        public const string MaterialIdRequired = "Material ID is required";
        public const string InvalidStatusTransition = "Invalid status transition";
        public const string CannotModifyNonDraftOrder = "Cannot modify orders that are not in draft status";
        public const string CannotModifyCompletedOrder = "Cannot modify completed orders";
    }
}