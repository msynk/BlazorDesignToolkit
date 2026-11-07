using AngleSharp.Dom;
using BlazorDesignToolkit.Testing;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorDesignToolkit.Primitives.Tests;

/// <summary>bUnit context with loose JS interop and the primitives module set up, plus accessibility helpers.</summary>
public abstract class PrimitiveTestBase : BunitContext
{
    protected PrimitiveTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Module = JSInterop.SetupModule(PrimitivesJs.ModulePath);
        Module.Mode = JSRuntimeMode.Loose;
        Module.Setup<int>("preventKeys", _ => true).SetResult(1);
        Module.Setup<int>("showDialog", _ => true).SetResult(2);
        Module.Setup<int>("showPopover", _ => true).SetResult(3);
        Module.Setup<int>("attachTooltip", _ => true).SetResult(4);
        Module.Setup<int>("trapFocus", _ => true).SetResult(5);
        Module.Setup<int>("registerHotkey", _ => true).SetResult(6);
        Module.Setup<string[]>("domOrder", _ => true).SetResult([]);
    }

    protected BunitJSModuleInterop Module { get; }

    protected static void AssertAccessible(string markup, params string[] ignore) => AccessibilityAssert.NoViolations(markup, ignoreRules: ignore);

    protected static KeyboardEventArgs Key(string key, bool shift = false, bool ctrl = false, bool alt = false)
        => new() { Key = key, ShiftKey = shift, CtrlKey = ctrl, AltKey = alt };

    protected int Calls(string identifier) => Module.Invocations.Count(i => i.Identifier == identifier);
}

internal static class DomExtensions
{
    public static string? Attr(this IElement element, string name) => element.GetAttribute(name);
}
