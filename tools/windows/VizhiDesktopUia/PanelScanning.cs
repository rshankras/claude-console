namespace VizhiDesktopUia;

internal static partial class Program
{
    // Fetch the controls used by panel/context checks, not the transcript and every diff
    // line. Keep all actionable nodes (including nested controls that make a button
    // ambiguous), document boundaries, project list items, tabs/panels, and modal/menu
    // boundaries. UIA promotes children of excluded layout nodes, preserving ancestry.
    // Approval, composer and status scans continue to request the raw tree.
    private static IUIAutomationCondition PanelTreeFilter()
    {
        var condition = Uia.CreatePropertyCondition(UiaIds.ControlType, 50032); // Window
        void Include(Int32 property, Object value) => condition =
            Uia.CreateOrCondition(condition, Uia.CreatePropertyCondition(property, value));
        foreach (var type in new[] { 50000, 50003, 50007, 50009, 50011, 50019, 50030, 50031 })
            Include(UiaIds.ControlType, type); // Button, ComboBox, ListItem, Menu, MenuItem, TabItem, Document, SplitButton
        foreach (var property in new[] { UiaIds.IsInvokeAvailable, UiaIds.IsExpandCollapseAvailable,
            UiaIds.IsToggleAvailable, UiaIds.IsSelectionItemAvailable }) Include(property, true);
        foreach (var role in new[] { "dialog", "alertdialog", "tabpanel" }) Include(UiaIds.AriaRole, role);
        return condition;
    }
}
