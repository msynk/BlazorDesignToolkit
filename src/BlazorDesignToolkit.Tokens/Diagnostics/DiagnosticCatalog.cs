using System.Collections.Frozen;

namespace BlazorDesignToolkit.Tokens.Diagnostics;

/// <summary>Describes a diagnostic rule: its stable identifier, category, default severity and guidance.</summary>
public sealed record DiagnosticDescriptor(
    string Id,
    string Title,
    string Category,
    DiagnosticSeverity DefaultSeverity,
    string Description,
    string? DefaultRemediation = null,
    bool IsSpecRequirement = false);

/// <summary>
/// The complete, stable list of rules emitted by the toolkit. Identifiers never change meaning;
/// retired rules are not reused. <see cref="DiagnosticDescriptor.IsSpecRequirement"/> marks rules that enforce
/// a normative DTCG 2025.10 requirement, as opposed to product conventions.
/// </summary>
public static class DiagnosticCatalog
{
    public const string ParsingCategory = "Parsing";
    public const string StructureCategory = "Structure";
    public const string ReferenceCategory = "References";
    public const string DesignSystemCategory = "DesignSystem";
    public const string AccessibilityCategory = "Accessibility";
    public const string ThemeCategory = "Themes";
    public const string ExportCategory = "Export";
    public const string CoverageCategory = "Coverage";
    public const string ConfigurationCategory = "Configuration";

    // ---- Parsing --------------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor InvalidJson = new("BDT1001", "Invalid JSON", ParsingCategory, DiagnosticSeverity.Error,
        "The document is not valid RFC 8259 JSON.", "Fix the JSON syntax at the reported location.", true);
    public static readonly DiagnosticDescriptor DuplicateKey = new("BDT1002", "Duplicate property name", ParsingCategory, DiagnosticSeverity.Error,
        "An object contains the same property name more than once; JSON parsers disagree on which value wins.",
        "Remove or rename one of the duplicate properties.");
    public static readonly DiagnosticDescriptor NestingTooDeep = new("BDT1003", "Nesting limit exceeded", ParsingCategory, DiagnosticSeverity.Error,
        "The document nests deeper than the configured maximum depth.", "Flatten the document or raise the configured MaxDepth if the input is trusted.");
    public static readonly DiagnosticDescriptor InputTooLarge = new("BDT1004", "Input size limit exceeded", ParsingCategory, DiagnosticSeverity.Error,
        "The input is larger than the configured maximum size.", "Split the token file or raise the configured MaxInputBytes if the input is trusted.");
    public static readonly DiagnosticDescriptor RootNotObject = new("BDT1005", "Root is not an object", ParsingCategory, DiagnosticSeverity.Error,
        "A design token document must be a JSON object.", null, true);

    // ---- Structure (DTCG format module) ---------------------------------------------------------------------
    public static readonly DiagnosticDescriptor InvalidName = new("BDT2001", "Invalid token or group name", StructureCategory, DiagnosticSeverity.Error,
        "Token and group names must not begin with '$' and must not contain '{', '}' or '.'.", "Rename the token or group.", true);
    public static readonly DiagnosticDescriptor TokenWithChildren = new("BDT2002", "Token contains children", StructureCategory, DiagnosticSeverity.Error,
        "An object with $value is a token and must not also contain child tokens or groups.", "Move the children into a group, or use a $root token.", true);
    public static readonly DiagnosticDescriptor UnknownReservedProperty = new("BDT2003", "Unknown reserved property", StructureCategory, DiagnosticSeverity.Warning,
        "A '$'-prefixed property is not defined by DTCG 2025.10. It is preserved but has no effect.", "Move vendor data into $extensions.");
    public static readonly DiagnosticDescriptor UnknownType = new("BDT2004", "Unknown token type", StructureCategory, DiagnosticSeverity.Error,
        "$type must be one of the types defined by DTCG 2025.10 (case-sensitive).", null, true);
    public static readonly DiagnosticDescriptor TypeUndetermined = new("BDT2005", "Token type cannot be determined", StructureCategory, DiagnosticSeverity.Error,
        "The token has no $type, is not an alias, and no parent group declares $type. Tools must not guess types.",
        "Add $type to the token or to a parent group.", true);
    public static readonly DiagnosticDescriptor InvalidValue = new("BDT2006", "Invalid token value", StructureCategory, DiagnosticSeverity.Error,
        "The $value does not match the shape required by the token's type.", null, true);
    public static readonly DiagnosticDescriptor UnknownValueProperty = new("BDT2007", "Unknown value property", StructureCategory, DiagnosticSeverity.Warning,
        "A value object contains a property not defined for its type.", "Remove the property or move it into $extensions.");
    public static readonly DiagnosticDescriptor ValueOutOfRange = new("BDT2008", "Value outside the specified range", StructureCategory, DiagnosticSeverity.Warning,
        "A numeric value lies outside the range described by the specification.");
    public static readonly DiagnosticDescriptor HexMismatch = new("BDT2009", "Hex fallback differs from components", StructureCategory, DiagnosticSeverity.Warning,
        "The color's hex fallback does not match its components after conversion to sRGB.", "Regenerate the hex fallback from the components.");
    public static readonly DiagnosticDescriptor InvalidPropertyType = new("BDT2010", "Invalid reserved property value", StructureCategory, DiagnosticSeverity.Error,
        "A reserved property has the wrong JSON type (for example $description must be a string).", null, true);
    public static readonly DiagnosticDescriptor UnsupportedConstruct = new("BDT2011", "Unsupported construct", StructureCategory, DiagnosticSeverity.Error,
        "The input uses a construct this toolkit does not support or that the specification leaves ambiguous. It is reported instead of being reinterpreted.");
    public static readonly DiagnosticDescriptor NamesDifferOnlyByCase = new("BDT2012", "Names differ only by case", StructureCategory, DiagnosticSeverity.Info,
        "Sibling names that differ only by case are valid but easily confused, and may collide in case-insensitive outputs.");
    public static readonly DiagnosticDescriptor InvalidExtends = new("BDT2013", "Invalid $extends", StructureCategory, DiagnosticSeverity.Error,
        "$extends must reference an existing group.", null, true);
    public static readonly DiagnosticDescriptor NonStandardUnit = new("BDT2014", "Non-standard unit", StructureCategory, DiagnosticSeverity.Warning,
        "The unit is accepted by configuration but is not defined by DTCG 2025.10; other tools may reject it.");
    public static readonly DiagnosticDescriptor PossiblyIncompleteToken = new("BDT2015", "Empty group looks like an incomplete token", StructureCategory, DiagnosticSeverity.Info,
        "The group has no children but declares token-like properties; a $value may be missing.");
    public static readonly DiagnosticDescriptor LegacyValueFormat = new("BDT2016", "Legacy value format", StructureCategory, DiagnosticSeverity.Warning,
        "The value uses a pre-2025.10 draft format (such as a hex color string or a '16px' dimension string). It was accepted because AllowLegacyValueFormats is enabled.",
        "Convert the value to the DTCG 2025.10 object form; 'bdt build' writes migrated values.");

    // ---- References -----------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor MissingReference = new("BDT3001", "Unresolved reference", ReferenceCategory, DiagnosticSeverity.Error,
        "A reference points to a token that does not exist.", null, true);
    public static readonly DiagnosticDescriptor CircularReference = new("BDT3002", "Circular reference", ReferenceCategory, DiagnosticSeverity.Error,
        "References must not be circular.", "Break the cycle by giving one token in the chain an explicit value.", true);
    public static readonly DiagnosticDescriptor ReferenceTargetsGroup = new("BDT3003", "Reference targets a group", ReferenceCategory, DiagnosticSeverity.Error,
        "Curly-brace references can only target tokens. Use '{group.$root}' to reference a group's root token.", null, true);
    public static readonly DiagnosticDescriptor DependsOnInvalidToken = new("BDT3004", "Depends on an invalid token", ReferenceCategory, DiagnosticSeverity.Warning,
        "The token cannot be resolved because a token it references is invalid. Fix the referenced token first.");
    public static readonly DiagnosticDescriptor CircularExtends = new("BDT3005", "Circular group inheritance", ReferenceCategory, DiagnosticSeverity.Error,
        "Groups must not create circular $extends chains.", null, true);
    public static readonly DiagnosticDescriptor ReferenceTypeMismatch = new("BDT3006", "Reference type mismatch", ReferenceCategory, DiagnosticSeverity.Error,
        "A reference resolves to a token whose type differs from the type required at that position.", null, true);
    public static readonly DiagnosticDescriptor InvalidJsonPointer = new("BDT3007", "Invalid JSON Pointer reference", ReferenceCategory, DiagnosticSeverity.Error,
        "A $ref JSON Pointer is malformed or does not resolve to a location inside a token value.", null, true);
    public static readonly DiagnosticDescriptor ReferenceDepthExceeded = new("BDT3008", "Reference chain too deep", ReferenceCategory, DiagnosticSeverity.Error,
        "The reference chain is longer than the configured maximum.", "Shorten the alias chain or raise MaxReferenceDepth.");
    public static readonly DiagnosticDescriptor InvalidReferenceSyntax = new("BDT3009", "Invalid reference syntax", ReferenceCategory, DiagnosticSeverity.Error,
        "A string looks like a reference but is not a valid '{group.token}' reference.", null, true);

    // ---- Design-system conventions --------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor NamingConvention = new("BDT4001", "Naming convention violation", DesignSystemCategory, DiagnosticSeverity.Warning,
        "A token name does not follow the configured naming convention.");
    public static readonly DiagnosticDescriptor MissingRequiredToken = new("BDT4002", "Missing required token", DesignSystemCategory, DiagnosticSeverity.Error,
        "A token required by the configured contract is not defined.");
    public static readonly DiagnosticDescriptor DuplicateValue = new("BDT4003", "Duplicate raw value", DesignSystemCategory, DiagnosticSeverity.Info,
        "Several tokens declare the same raw value instead of aliasing a single source token.", "Alias one token to the other, or document why both exist.");
    public static readonly DiagnosticDescriptor InconsistentScale = new("BDT4004", "Inconsistent scale", DesignSystemCategory, DiagnosticSeverity.Warning,
        "Tokens in a numbered scale are not ordered consistently with their names.");
    public static readonly DiagnosticDescriptor DeprecatedReference = new("BDT4005", "Reference to deprecated token", DesignSystemCategory, DiagnosticSeverity.Warning,
        "A token references a deprecated token.");
    public static readonly DiagnosticDescriptor LayerViolation = new("BDT4006", "Token layer violation", DesignSystemCategory, DiagnosticSeverity.Warning,
        "A token does not follow the configured primitive / semantic / component layering.");
    public static readonly DiagnosticDescriptor RequiredTokenWrongType = new("BDT4007", "Required token has wrong type", DesignSystemCategory, DiagnosticSeverity.Error,
        "A token required by the configured contract exists but has a different type.");
    public static readonly DiagnosticDescriptor DeprecatedToken = new("BDT4008", "Deprecated token", DesignSystemCategory, DiagnosticSeverity.Info,
        "The token is marked $deprecated.");

    // ---- Accessibility --------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor TextContrast = new("BDT5001", "Insufficient text contrast", AccessibilityCategory, DiagnosticSeverity.Error,
        "Text color and background do not meet WCAG 2.2 SC 1.4.3 Contrast (Minimum): 4.5:1, or 3:1 for large text.");
    public static readonly DiagnosticDescriptor NonTextContrast = new("BDT5002", "Insufficient non-text contrast", AccessibilityCategory, DiagnosticSeverity.Error,
        "UI component or graphical object colors do not meet WCAG 2.2 SC 1.4.11 Non-text Contrast (3:1).");
    public static readonly DiagnosticDescriptor FocusIndicatorContrast = new("BDT5003", "Insufficient focus indicator contrast", AccessibilityCategory, DiagnosticSeverity.Error,
        "The focus indicator color does not reach 3:1 against an adjacent background (WCAG 2.2 SC 1.4.11; see also SC 2.4.13).");
    public static readonly DiagnosticDescriptor MissingColorPair = new("BDT5004", "Missing foreground/background pairing", AccessibilityCategory, DiagnosticSeverity.Warning,
        "A background token has no matching foreground token under the configured pairing convention.");
    public static readonly DiagnosticDescriptor ContrastIndeterminate = new("BDT5005", "Contrast cannot be determined", AccessibilityCategory, DiagnosticSeverity.Warning,
        "The rendered colors cannot be established reliably (translucent background without backdrop, out-of-gamut color, or 'none' components), so no pass/fail verdict is given.");
    public static readonly DiagnosticDescriptor EnhancedTextContrast = new("BDT5006", "Enhanced text contrast not met (AAA)", AccessibilityCategory, DiagnosticSeverity.Warning,
        "Text does not meet WCAG 2.2 SC 1.4.6 Contrast (Enhanced): 7:1, or 4.5:1 for large text. Optional AAA rule.");
    public static readonly DiagnosticDescriptor InvalidContrastPair = new("BDT5007", "Invalid contrast pair configuration", AccessibilityCategory, DiagnosticSeverity.Error,
        "A configured contrast pair references a missing, invalid or non-color token.");

    // ---- Themes ---------------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor OverrideAddsToken = new("BDT6001", "Override introduces a new token", ThemeCategory, DiagnosticSeverity.Warning,
        "A theme layer defines a token that does not exist in the base layers; overrides are expected to replace existing tokens.");
    public static readonly DiagnosticDescriptor OverrideTypeMismatch = new("BDT6002", "Override changes token type", ThemeCategory, DiagnosticSeverity.Error,
        "A theme layer overrides a token with a value of a different type.");
    public static readonly DiagnosticDescriptor ThemeNotFound = new("BDT6003", "Theme not found", ThemeCategory, DiagnosticSeverity.Error,
        "A referenced theme is not registered.");
    public static readonly DiagnosticDescriptor CircularThemeInheritance = new("BDT6004", "Circular theme inheritance", ThemeCategory, DiagnosticSeverity.Error,
        "Themes must not inherit from themselves directly or indirectly.");
    public static readonly DiagnosticDescriptor IncompleteThemeContext = new("BDT6005", "Incomplete theme context", ThemeCategory, DiagnosticSeverity.Warning,
        "Contexts of the same theme axis define different sets of tokens, so switching contexts leaves some tokens unchanged.");
    public static readonly DiagnosticDescriptor InvalidResolverDocument = new("BDT6006", "Invalid resolver document", ThemeCategory, DiagnosticSeverity.Error,
        "The DTCG resolver document violates the resolver module.", null, true);
    public static readonly DiagnosticDescriptor InvalidResolverInput = new("BDT6007", "Invalid resolver input", ThemeCategory, DiagnosticSeverity.Error,
        "Resolver inputs must name declared modifiers and valid contexts, and every modifier without a default must receive an input.", null, true);
    public static readonly DiagnosticDescriptor ResolverSingleContext = new("BDT6008", "Modifier with a single context", ThemeCategory, DiagnosticSeverity.Warning,
        "A resolver modifier should declare two or more contexts.", null, true);
    public static readonly DiagnosticDescriptor DuplicateTheme = new("BDT6009", "Duplicate theme", ThemeCategory, DiagnosticSeverity.Error,
        "Two themes share the same identifier.");
    public static readonly DiagnosticDescriptor InvalidThemeSelection = new("BDT6010", "Invalid theme selection", ThemeCategory, DiagnosticSeverity.Error,
        "A selected axis value is not declared by the theme.");

    // ---- Export ---------------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor UnsupportedConversion = new("BDT7001", "Unsupported conversion", ExportCategory, DiagnosticSeverity.Warning,
        "The token cannot be represented in the export target and was skipped.");
    public static readonly DiagnosticDescriptor NameCollision = new("BDT7002", "Output name collision", ExportCategory, DiagnosticSeverity.Error,
        "Two tokens map to the same output name.", "Rename one token or adjust the naming options.");
    public static readonly DiagnosticDescriptor OutOfGamut = new("BDT7003", "Color outside target gamut", ExportCategory, DiagnosticSeverity.Warning,
        "The color cannot be represented exactly in the requested output format and was gamut-clipped.");
    public static readonly DiagnosticDescriptor InvalidMapping = new("BDT7004", "Invalid integration mapping", ExportCategory, DiagnosticSeverity.Error,
        "An integration mapping references an unknown token or unknown target variable.");
    public static readonly DiagnosticDescriptor MappingTypeMismatch = new("BDT7005", "Integration mapping type mismatch", ExportCategory, DiagnosticSeverity.Error,
        "A token mapped to an integration variable does not have the type that variable requires.");
    public static readonly DiagnosticDescriptor InvalidIdentifier = new("BDT7006", "Invalid identifier", ExportCategory, DiagnosticSeverity.Error,
        "A configured prefix, theme identifier or attribute name is not a valid identifier for the target.");

    // ---- Coverage -------------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor UnusedToken = new("BDT8001", "Unused token", CoverageCategory, DiagnosticSeverity.Info,
        "The token is not referenced by other tokens and no usage was found in the analysed sources.");
    public static readonly DiagnosticDescriptor UndefinedCustomProperty = new("BDT8002", "Undefined custom property", CoverageCategory, DiagnosticSeverity.Warning,
        "A source uses a CSS custom property with the token prefix that no token produces.");
    public static readonly DiagnosticDescriptor MissingContractToken = new("BDT8003", "Missing component contract token", CoverageCategory, DiagnosticSeverity.Error,
        "A component manifest requires a token that is not defined.");
    public static readonly DiagnosticDescriptor UnknownManifestToken = new("BDT8004", "Unknown manifest token", CoverageCategory, DiagnosticSeverity.Warning,
        "A component manifest lists an optional token that is not defined.");
    public static readonly DiagnosticDescriptor AnalysisLimit = new("BDT8005", "Analysis limitation", CoverageCategory, DiagnosticSeverity.Info,
        "Part of the source could not be analysed statically (for example dynamically built custom-property names).");
    public static readonly DiagnosticDescriptor UndeclaredTokenUsage = new("BDT8006", "Undeclared token usage", CoverageCategory, DiagnosticSeverity.Info,
        "A component's sources use a token that its manifest does not declare.");

    // ---- Configuration --------------------------------------------------------------------------------------
    public static readonly DiagnosticDescriptor InvalidConfiguration = new("BDT9001", "Invalid configuration", ConfigurationCategory, DiagnosticSeverity.Error,
        "The configuration file is invalid.");
    public static readonly DiagnosticDescriptor SuppressionWithoutJustification = new("BDT9002", "Suppression without justification", ConfigurationCategory, DiagnosticSeverity.Error,
        "Every suppression must state a justification.");
    public static readonly DiagnosticDescriptor FileNotFound = new("BDT9003", "File not found", ConfigurationCategory, DiagnosticSeverity.Error,
        "An input file or glob did not match any file.");
    public static readonly DiagnosticDescriptor OutputOutOfDate = new("BDT9004", "Generated output out of date", ConfigurationCategory, DiagnosticSeverity.Error,
        "A generated output on disk differs from what the current tokens produce.", "Run 'bdt export' and commit the result.");

    private static readonly FrozenDictionary<string, DiagnosticDescriptor> s_byId = typeof(DiagnosticCatalog)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(static f => f.FieldType == typeof(DiagnosticDescriptor))
        .Select(static f => (DiagnosticDescriptor)f.GetValue(null)!)
        .ToFrozenDictionary(static d => d.Id, StringComparer.Ordinal);

    /// <summary>All descriptors ordered by identifier.</summary>
    public static IReadOnlyList<DiagnosticDescriptor> All { get; } = s_byId.Values.OrderBy(static d => d.Id, StringComparer.Ordinal).ToArray();

    public static DiagnosticDescriptor? TryGet(string id) => s_byId.GetValueOrDefault(id);
}
