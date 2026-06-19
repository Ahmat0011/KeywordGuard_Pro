using KeywordGuard.Pro.Security;

namespace KeywordGuard.Pro.UI.Services;

/// <summary>
/// UI-seitige Überwachung ist absichtlich deaktiviert.
/// Die Durchsetzung erfolgt zentral durch den Agenten.
/// </summary>
public class WordWatcher
{
    public void Start(Func<List<BlockedItem>> itemsProvider, Func<bool> isActive)
    {
        // No-op: UI führt keine versteckte Fensterüberwachung mehr aus.
    }

    public void Stop()
    {
        // No-op.
    }
}
