using System.IO;
using System.Windows.Input;
using LineByLine.Documents;
using LineByLine.Services;

namespace LineByLine.Tests;

public class LineDetectionTests
{
    private static readonly string SamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "sample-invoice.pdf");

    [Fact]
    public void SampleInvoice_HasTwoPagesWithText()
    {
        var doc = PdfDoc.Open(SamplePath);

        Assert.Equal(2, doc.Pages.Count);
        Assert.All(doc.Pages, p => Assert.False(p.RowsAreFallback));
        Assert.Equal(612, doc.Pages[0].Width, 0);
        Assert.Equal(792, doc.Pages[0].Height, 0);
    }

    [Fact]
    public void EachLineItem_IsItsOwnRow()
    {
        var doc = PdfDoc.Open(SamplePath);
        var rows = doc.Pages[0].Rows;

        // Every item row holds its SKU, description and amounts, all on one line.
        var dump = string.Join(" | ", rows.Select(r => $"{r.Top:F1}-{r.Bottom:F1}: {r.Text}"));
        var paper = rows.SingleOrDefault(r => r.Text.Contains("PA-2011"));
        Assert.True(paper != null, dump);
        Assert.Contains("Copy paper", paper.Text);
        Assert.Contains("425.00", paper.Text);

        var toner = Assert.Single(rows, r => r.Text.Contains("TN-0458"));
        Assert.True(toner.Text.Contains("359.96"), toner.Text);
        Assert.True(toner.Top > paper.Bottom, "rows should be in reading order");
    }

    [Fact]
    public void HeaderBlocks_AtDifferentHeights_DoNotShred()
    {
        var doc = PdfDoc.Open(SamplePath);
        var rows = doc.Pages[0].Rows;

        // "Northwind Supply Co." (left) and "INVOICE" (right) sit at slightly different heights.
        Assert.Single(rows, r => r.Text.Contains("Northwind"));
        Assert.Single(rows, r => r.Text.Contains("INVOICE"));
        Assert.All(rows, r => Assert.True(r.Text.Length > 1, $"stray fragment row: '{r.Text}'"));
    }

    [Fact]
    public void Render_ProducesOpaqueBitmapAtRequestedScale()
    {
        var doc = PdfDoc.Open(SamplePath);
        var bmp = doc.Render(0, 1.5);

        Assert.Equal(918, bmp.PixelWidth);
        Assert.Equal(1188, bmp.PixelHeight);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Down", ModifierKeys.Control | ModifierKeys.Alt, Key.Down)]
    [InlineData("Ctrl+Alt+PageDown", ModifierKeys.Control | ModifierKeys.Alt, Key.PageDown)]
    [InlineData("Alt+Shift+F9", ModifierKeys.Alt | ModifierKeys.Shift, Key.F9)]
    public void ParsesAndRoundTrips(string text, ModifierKeys mods, Key key)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(mods, key), hk);
        Assert.Equal(text, hk.ToStorageString());
    }

    [Fact]
    public void DisplayUsesArrows()
    {
        Assert.Equal("Ctrl+Alt+↓", Hotkey.Defaults[HotkeyAction.NextLines].ToDisplayString());
    }

    [Fact]
    public void ShortcutsWithoutCtrlOrAltAreRejected()
    {
        Assert.False(new Hotkey(ModifierKeys.Shift, Key.Down).IsValidGlobal);
        Assert.False(new Hotkey(ModifierKeys.None, Key.J).IsValidGlobal);
        Assert.True(new Hotkey(ModifierKeys.Control, Key.J).IsValidGlobal);
    }

    [Fact]
    public void NumberPadKeysHaveReadableNames()
    {
        Assert.Equal("Ctrl+Alt+Num 2", new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.NumPad2).ToDisplayString());
        Assert.Equal("Ctrl+Alt+Num +", new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Add).ToDisplayString());
        Assert.Equal("Ctrl+Alt+.", Hotkey.Defaults[HotkeyAction.HighlightLine].ToDisplayString());
        Assert.True(Hotkey.TryParse("Ctrl+Alt+NumPad8", out var hk));
        Assert.Equal(Key.NumPad8, hk.Key);
    }

    [Fact]
    public void SecondShortcuts_DefaultToTheNumberPad_AndCanBeCleared()
    {
        var s = new AppSettings();
        var bindings = s.AllBindings().ToList();

        Assert.Contains((HotkeyAction.HighlightLine, new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Add)), bindings);
        Assert.Contains((HotkeyAction.NextLines, new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.NumPad2)), bindings);
        Assert.Contains((HotkeyAction.NextLines, Hotkey.Defaults[HotkeyAction.NextLines]), bindings); // main one still there
        Assert.Null(s.GetSecondHotkey(HotkeyAction.ToggleDim));

        s.SetSecondHotkey(HotkeyAction.HighlightLine, null);
        Assert.Null(s.GetSecondHotkey(HotkeyAction.HighlightLine));
        Assert.DoesNotContain(s.AllBindings(), b => b.Hotkey.Key == Key.Add);

        // Every default shortcut, main or second, is unique.
        Assert.Equal(bindings.Count, bindings.Select(b => b.Hotkey).Distinct().Count());
    }

    [Fact]
    public void StepFollowsLinesUntilSetExplicitly()
    {
        var s = new AppSettings { LinesInView = 3 };
        Assert.Equal(3, s.EffectiveStep);
        s.StepSize = 1;
        Assert.Equal(1, s.EffectiveStep);
    }
}
