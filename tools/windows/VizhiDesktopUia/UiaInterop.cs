// UI Automation through COM — the same choice, for the same reason, as ClaudeConsoleFocus:
// UIAutomationCore.dll ships with Windows, while the System.Windows.Automation wrapper lives in
// the Desktop Runtime and made a helper either framework-dependent and broken on clean machines
// (#83) or self-contained and 68 MB.
//
// Only the vtable slots this helper calls are declared; a `_VtblGapN_M` method reserves the M
// slots in between by count — the device tlbimp uses, honoured by the runtime's built-in COM
// interop. Every order and id below was checked against the type library inside
// UIAutomationCore.dll (2026-09-30), not written from memory. Never "tidy" the order.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VizhiDesktopUia;

internal static class UiaIds
{
    public const Int32 TreeScopeSubtree = 7;
    public const Int32 ElementModeFull = 1;

    // Properties
    public const Int32 ProcessId = 30002;
    public const Int32 ControlType = 30003;
    public const Int32 Name = 30005;
    public const Int32 IsEnabled = 30010;
    public const Int32 AutomationId = 30011;
    public const Int32 ClassName = 30012;
    public const Int32 HelpText = 30013;
    public const Int32 NativeWindowHandle = 30020;
    public const Int32 IsOffscreen = 30022;
    public const Int32 IsExpandCollapseAvailable = 30028;
    public const Int32 IsInvokeAvailable = 30031;
    public const Int32 IsSelectionItemAvailable = 30036;
    public const Int32 IsToggleAvailable = 30041;
    public const Int32 IsValueAvailable = 30043;
    public const Int32 ValueValue = 30045;
    public const Int32 ValueIsReadOnly = 30046;
    public const Int32 ExpandCollapseState = 30070;
    public const Int32 SelectionItemIsSelected = 30079;
    public const Int32 LegacyDescription = 30094;
    public const Int32 AriaRole = 30101;
    public const Int32 AriaProperties = 30102;
    public const Int32 FullDescription = 30159;

    // Patterns
    public const Int32 InvokePattern = 10000;
    public const Int32 ValuePattern = 10002;
    public const Int32 ExpandCollapsePattern = 10005;
    public const Int32 SelectionItemPattern = 10010;
    public const Int32 TogglePattern = 10015;

    // ExpandCollapseState
    public const Int32 Collapsed = 0;
    public const Int32 Expanded = 1;

    /// <summary>Stable role words for the wire; control type ids never leave this helper.</summary>
    public static String Role(Int32 controlType) => controlType switch
    {
        50000 => "Button",
        50002 => "CheckBox",
        50003 => "ComboBox",
        50004 => "Edit",
        50005 => "Hyperlink",
        50006 => "Image",
        50007 => "ListItem",
        50008 => "List",
        50009 => "Menu",
        50010 => "MenuBar",
        50011 => "MenuItem",
        50012 => "ProgressBar",
        50013 => "RadioButton",
        50017 => "StatusBar",
        50018 => "Tab",
        50019 => "TabItem",
        50020 => "Text",
        50021 => "ToolBar",
        50022 => "ToolTip",
        50023 => "Tree",
        50024 => "TreeItem",
        50025 => "Custom",
        50026 => "Group",
        50030 => "Document",
        50031 => "SplitButton",
        50032 => "Window",
        50033 => "Pane",
        50036 => "Table",
        50037 => "TitleBar",
        50038 => "Separator",
        _ => "Other",
    };
}

[ComImport, Guid("ff48dba4-60ef-4201-aa87-54103eef594e")]
internal class CUIAutomation
{
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    void _VtblGap1_3();                                             // CompareElements, CompareRuntimeIds, GetRootElement
    IUIAutomationElement ElementFromHandle(IntPtr hwnd);
    void _VtblGap2_3();                                             // ElementFromPoint, GetFocusedElement, GetRootElementBuildCache
    IUIAutomationElement ElementFromHandleBuildCache(IntPtr hwnd, IUIAutomationCacheRequest cacheRequest);
    void _VtblGap3_6();                                             // ElementFromPointBuildCache … RawViewWalker
    IUIAutomationCondition RawViewCondition { get; }
    void _VtblGap4_2();                                             // ControlViewCondition, ContentViewCondition
    IUIAutomationCacheRequest CreateCacheRequest();
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    void SetFocus();
    void _VtblGap1_5();                                             // GetRuntimeId … FindAllBuildCache
    IUIAutomationElement BuildUpdatedCache(IUIAutomationCacheRequest cacheRequest);
    [return: MarshalAs(UnmanagedType.Struct)]
    Object? GetCurrentPropertyValue(Int32 propertyId);
    void _VtblGap2_1();                                             // GetCurrentPropertyValueEx
    [return: MarshalAs(UnmanagedType.Struct)]
    Object? GetCachedPropertyValue(Int32 propertyId);
    void _VtblGap3_3();                                             // GetCachedPropertyValueEx, GetCurrentPatternAs, GetCachedPatternAs
    [return: MarshalAs(UnmanagedType.IUnknown)]
    Object? GetCurrentPattern(Int32 patternId);
    void _VtblGap4_2();                                             // GetCachedPattern, GetCachedParent
    IUIAutomationElementArray? GetCachedChildren();
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("b32a92b5-bc25-4078-9c08-d7ee95c48e03"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCacheRequest
{
    void AddProperty(Int32 propertyId);
    void _VtblGap1_2();                                             // AddPattern, Clone
    Int32 TreeScope { get; set; }
    IUIAutomationCondition TreeFilter { get; set; }
    Int32 AutomationElementMode { get; set; }
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElementArray
{
    Int32 Length { get; }
    IUIAutomationElement GetElement(Int32 index);
}

[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationCondition
{
}

[ComImport, Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationInvokePattern
{
    void Invoke();
}

[ComImport, Guid("619be086-1f4e-4ee4-bafa-210128738730"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationExpandCollapsePattern
{
    void Expand();
    void Collapse();
    Int32 CurrentExpandCollapseState { get; }
}

[ComImport, Guid("94cf8058-9b8d-4ab9-8bfd-4cd0a33c8c70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTogglePattern
{
    void Toggle();
}

[ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationSelectionItemPattern
{
    void Select();
}

[ComImport, Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationValuePattern
{
    void SetValue([MarshalAs(UnmanagedType.BStr)] String value);
    String? CurrentValue { [return: MarshalAs(UnmanagedType.BStr)] get; }
    Int32 CurrentIsReadOnly { get; }
}
