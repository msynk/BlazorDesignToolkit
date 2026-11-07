using BlazorDesignToolkit.Testing;

namespace BlazorDesignToolkit.Primitives.Tests;

public class KeyboardLogicTests
{
    [Theory]
    [InlineData("ArrowRight", Orientation.Horizontal, TextDirection.Ltr, NavigationIntent.Next)]
    [InlineData("ArrowRight", Orientation.Horizontal, TextDirection.Rtl, NavigationIntent.Previous)]
    [InlineData("ArrowDown", Orientation.Horizontal, TextDirection.Ltr, NavigationIntent.None)]
    [InlineData("ArrowDown", Orientation.Vertical, TextDirection.Ltr, NavigationIntent.Next)]
    [InlineData("ArrowLeft", Orientation.Vertical, TextDirection.Ltr, NavigationIntent.None)]
    [InlineData("Home", Orientation.Vertical, TextDirection.Ltr, NavigationIntent.First)]
    [InlineData("End", Orientation.Horizontal, TextDirection.Rtl, NavigationIntent.Last)]
    public void Intent_respects_orientation_and_direction(string key, Orientation orientation, TextDirection direction, NavigationIntent expected)
        => Assert.Equal(expected, KeyboardNavigation.Intent(key, orientation, direction));

    [Theory]
    [InlineData(0, NavigationIntent.Next, true, 2)]
    [InlineData(3, NavigationIntent.Next, true, 0)]
    [InlineData(3, NavigationIntent.Next, false, 3)]
    [InlineData(0, NavigationIntent.Previous, true, 3)]
    [InlineData(-1, NavigationIntent.Next, true, 0)]
    [InlineData(2, NavigationIntent.First, true, 0)]
    [InlineData(0, NavigationIntent.Last, true, 3)]
    public void Move_skips_disabled_items(int current, NavigationIntent intent, bool loop, int expected)
    {
        var disabled = new[] { false, true, false, false };
        Assert.Equal(expected, KeyboardNavigation.Move(current, 4, i => disabled[i], intent, loop));
    }

    [Fact]
    public void Move_returns_minus_one_when_everything_is_disabled()
        => Assert.Equal(-1, KeyboardNavigation.Move(-1, 3, _ => true, NavigationIntent.First));

    [Fact]
    public void Typeahead_matches_prefix_cycles_and_resets_after_timeout()
    {
        var time = new ManualTime();
        var typeahead = new Typeahead(time);
        string[] labels = ["Apple", "Avocado", "Banana", "Blueberry", "Cherry"];
        Assert.Equal(2, typeahead.Search('b', -1, labels, _ => false));
        Assert.Equal(3, typeahead.Search('l', 2, labels, _ => false)); // "bl"
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, typeahead.Search('a', 3, labels, _ => false));
        Assert.Equal(1, typeahead.Search('a', 0, labels, _ => false)); // repeated letter cycles
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(4, typeahead.Search('c', 1, labels, i => false));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, typeahead.Search('z', 1, labels, _ => false)); // no match keeps current
    }

    [Fact]
    public void Ids_are_unique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => Ids.Next("x")).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}

/// <summary>Proves that each audit rule detects what it claims to detect, so passing audits are meaningful.</summary>
public class AuditRuleTests
{
    [Theory]
    [InlineData("<div id=\"a\"></div><span id=\"a\"></span>", "duplicate-id")]
    [InlineData("<button aria-describedby=\"missing\">x</button>", "broken-idref")]
    [InlineData("<div role=\"listbox\" aria-label=\"l\" tabindex=\"0\" aria-activedescendant=\"o\"></div><div id=\"o\" role=\"option\">x</div>", "activedescendant-scope")]
    [InlineData("<label for=\"d\">x</label><div id=\"d\"></div>", "label-target")]
    [InlineData("<div role=\"buton\"></div>", "invalid-role")]
    [InlineData("<button aria-expanded=\"yes\">x</button>", "invalid-aria-value")]
    [InlineData("<div role=\"switch\" tabindex=\"0\" aria-label=\"x\"></div>", "required-state")]
    [InlineData("<div role=\"combobox\" aria-label=\"x\"></div>", "required-state")]
    [InlineData("<div role=\"tab\">x</div>", "required-context")]
    [InlineData("<button></button>", "accessible-name")]
    [InlineData("<input type=\"text\">", "accessible-name")]
    [InlineData("<div aria-hidden=\"true\"><button>x</button></div>", "focusable-hidden")]
    [InlineData("<button>x <a href=\"#\">y</a></button>", "nested-interactive")]
    [InlineData("<ol><li role=\"alert\">x</li></ol>", "list-children")]
    public void Each_rule_detects_its_problem(string html, string rule)
        => Assert.Contains(AccessibilityAudit.Audit(html), i => i.Rule == rule);

    [Theory]
    [InlineData("<label>Name <input type=\"text\"></label>")]
    [InlineData("<label for=\"n\">Name</label><input id=\"n\" type=\"text\">")]
    [InlineData("<span id=\"l\">Size</span><div role=\"radiogroup\" aria-labelledby=\"l\"><label><input type=\"radio\" name=\"s\"> S</label></div>")]
    [InlineData("<button aria-label=\"Close\"><svg aria-hidden=\"true\"></svg></button>")]
    [InlineData("<div role=\"tablist\" aria-label=\"t\"><button role=\"tab\" aria-selected=\"true\">A</button></div>")]
    [InlineData("<ul role=\"tree\" aria-label=\"t\"><li role=\"treeitem\" aria-selected=\"false\">a<ul role=\"group\"><li role=\"treeitem\" aria-selected=\"false\">b</li></ul></li></ul>")]
    public void Correct_markup_passes(string html) => AccessibilityAssert.NoViolations(html);

    [Fact]
    public void Positive_tabindex_is_a_warning_only()
    {
        var issues = AccessibilityAudit.Audit("<button tabindex=\"3\">x</button>");
        Assert.Equal(AuditSeverity.Warning, Assert.Single(issues).Severity);
        AccessibilityAssert.NoViolations("<button tabindex=\"3\">x</button>");
        Assert.Throws<AccessibilityException>(() => AccessibilityAssert.NoViolations("<button tabindex=\"3\">x</button>", failOnWarnings: true));
    }
}
