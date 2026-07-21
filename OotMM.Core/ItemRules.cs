namespace OotMM.Core;

public static class ItemRules
{
    public static bool IsTrapItemId(string itemId) =>
        itemId.Contains("_TRAP_", StringComparison.Ordinal);
}
