namespace BlazorDesignToolkit.Primitives;

/// <summary>
/// Relationships between a form control and its label, description and error message. Cascaded by <see cref="FormField"/>
/// and consumed by every form primitive (and available to custom inputs through <see cref="ControlAttributes"/>).
/// </summary>
public sealed class FormFieldContext : PrimitiveContext
{
    internal FormFieldContext(string baseId) => BaseId = baseId;

    public string BaseId { get; }
    public string ControlId => BaseId + "-control";
    public string LabelId => BaseId + "-label";
    public string DescriptionId => BaseId + "-description";
    public string ErrorId => BaseId + "-error";

    public bool Invalid { get; internal set; }
    public bool Required { get; internal set; }
    public bool Disabled { get; internal set; }
    public bool HasDescription { get; internal set; }
    public bool HasError { get; internal set; }
    /// <summary>False when the control is not a labelable element (radio group, listbox); the label is then referenced only through aria-labelledby.</summary>
    public bool LabelTargetsControl { get; internal set; } = true;

    /// <summary>The value for <c>aria-describedby</c>: description and (when invalid) the error message.</summary>
    public string? DescribedBy
    {
        get
        {
            var ids = new List<string>(2);
            if (HasDescription) ids.Add(DescriptionId);
            if (Invalid && HasError) ids.Add(ErrorId);
            return ids.Count == 0 ? null : string.Join(' ', ids);
        }
    }

    /// <summary>Attributes to splat on a custom control element: id, aria-describedby, aria-invalid, aria-errormessage, required, disabled.</summary>
    public IReadOnlyDictionary<string, object> ControlAttributes
    {
        get
        {
            var attributes = new Dictionary<string, object> { ["id"] = ControlId };
            if (DescribedBy is { } d) attributes["aria-describedby"] = d;
            if (Invalid)
            {
                attributes["aria-invalid"] = "true";
                if (HasError) attributes["aria-errormessage"] = ErrorId;
            }
            if (Required) attributes["required"] = true;
            if (Disabled) attributes["disabled"] = true;
            return attributes;
        }
    }

    internal void NotifyChanged() => NotifyStateChanged();
}
