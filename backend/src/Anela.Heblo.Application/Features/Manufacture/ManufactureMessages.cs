namespace Anela.Heblo.Application.Features.Manufacture;

internal static class ManufactureMessages
{
    public const string UnexpectedSemiProductError =
        "Došlo k neočekávané chybě při potvrzení výroby polotovaru";

    public const string UnexpectedProductCompletionErrorFormat =
        "Došlo k neočekávané chybě při dokončení výroby produktů: {0}";

    public const string QuantityUpdateErrorFormat =
        "Chyba při aktualizaci množství: {0}";

    public const string ProductQuantityUpdateErrorFormat =
        "Chyba při aktualizaci množství produktů: {0}";

    public const string StatusChangeErrorFormat =
        "Chyba při změně stavu: {0}";

    public const string SemiProductManufacturedSuccessFormat =
        "Polotovar byl úspěšně vyroben se skutečným množstvím {0}";

    public const string SemiProductDefaultChangeReasonFormat =
        "Potvrzeno skutečné množství polotovaru: {0}";

    public const string SemiProductErpNoteFormat =
        "Vytvořena vydaná objednávka meziproduktu {0}";

    public const string ProductCompletionDefaultChangeReason =
        "Potvrzeno dokončení výroby produktů";

    public const string ProductCompletionDefaultNoteFormat =
        "Potvrzeno dokončení výroby produktů - {0}";

    public const string WeightToleranceOverrideFormat =
        "Hmotnost mimo toleranci potvrzena uživatelem. Rozdíl: {0:F2}% (povoleno: {1:F2}%)";
}
