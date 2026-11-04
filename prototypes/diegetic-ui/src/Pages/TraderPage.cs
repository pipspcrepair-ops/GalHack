using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Logic;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using Godot;

namespace DiegeticUi.Pages;

/// <summary>
/// The trader's terminal. Left: what the depot buys (wanted items in solid yellow) and sells.
/// Middle and right: your suit and your ship's hold, side by side, with the same wanted items
/// lit up and a sell-all button under each that shows what it pays before you press it.
/// Click a row to buy or sell one.
/// </summary>
public partial class TraderPage : Control, IScreenPage
{
    static Palette C => UiTheme.Colors;

    readonly Trader _trader;
    readonly Inventory _suit, _hold;
    readonly Wallet _wallet;
    readonly WarningTimeline _warnings;
    readonly RowList _buys, _sells, _suitList, _holdList;
    public BlockButton SellSuit { get; }
    public BlockButton SellHold { get; }
    (int, int, int, int, int) _seen = (-1, -1, -1, -1, -1);
    string _message = "WANTED ITEMS PAY A PREMIUM";

    public TraderPage(Trader trader, Inventory suit, Inventory hold, Wallet wallet, WarningTimeline warnings)
    {
        _trader = trader;
        _suit = suit;
        _hold = hold;
        _wallet = wallet;
        _warnings = warnings;
        MouseFilter = MouseFilterEnum.Ignore;

        _buys = Add(new RowList { RowHeight = 40, FontSize = 22 }, new Rect2(24, 196, 400, 270));
        _sells = Add(new RowList { RowHeight = 40, FontSize = 22 }, new Rect2(24, 520, 400, 184));
        _suitList = Add(new RowList { RowHeight = 40, FontSize = 22 }, new Rect2(448, 196, 396, 400));
        _holdList = Add(new RowList { RowHeight = 40, FontSize = 22 }, new Rect2(868, 196, 388, 400));
        SellSuit = Add(new BlockButton { Colour = C.Accent2 }, new Rect2(448, 618, 396, 86));
        SellHold = Add(new BlockButton { Colour = C.Accent2 }, new Rect2(868, 618, 388, 86));

        _sells.Clicked += row =>
        {
            var offer = (SellOffer)row.Tag;
            _message = _trader.BuyOne(offer, _suit, _wallet) ? $"BOUGHT 1 {offer.Item.Name}" : "NOT ENOUGH CREDITS OR ROOM";
        };
        _suitList.Clicked += row => SellOne(_suit, (ItemDef)row.Tag);
        _holdList.Clicked += row => SellOne(_hold, (ItemDef)row.Tag);
        SellSuit.Pressed += () => SellAll(_suit);
        SellHold.Pressed += () => SellAll(_hold);
    }

    T Add<T>(T widget, Rect2 rect) where T : Control
    {
        widget.Position = rect.Position;
        widget.Size = rect.Size;
        AddChild(widget);
        return widget;
    }

    void SellOne(Inventory from, ItemDef item)
    {
        if (_trader.SellOne(from, item, _wallet)) _message = $"SOLD 1 {item.Name}  +{_trader.PriceFor(item)}";
    }

    void SellAll(Inventory from)
    {
        int earned = _trader.SellAllWanted(from, _wallet);
        _message = earned > 0 ? $"SOLD ALL WANTED FROM {from.Name}  +{UiTheme.Credits(earned)} CR" : "NOTHING WANTED THERE";
    }

    public bool Refresh()
    {
        var seen = (_trader.Version, _suit.Version, _hold.Version, _wallet.Version, _warnings.Version);
        if (seen == _seen) return false;
        _seen = seen;

        _buys.SetRows(_trader.Buys.Select(b => new RowItem(b.Item.Name, $"{b.Price}", C.Accent2, b.Wanted, false, b.Item,
            b.Wanted ? "WANTED" : null)).ToList());
        _sells.SetRows(_trader.Sells.Select(s => new RowItem(s.Item.Name, $"{s.Price}", C.Good, false, s.Stock > 0, s,
            $"x{s.Stock}")).ToList());
        _suitList.SetRows(Rows(_suit));
        _holdList.SetRows(Rows(_hold));

        int suitValue = _trader.WantedValue(_suit), holdValue = _trader.WantedValue(_hold);
        SellSuit.Text = "SELL ALL WANTED";
        SellSuit.Sub = suitValue > 0 ? $"+{UiTheme.Credits(suitValue)} CR" : "NOTHING WANTED";
        SellSuit.Enabled = suitValue > 0;
        SellSuit.Selected = suitValue > 0;
        SellHold.Text = "SELL ALL WANTED";
        SellHold.Sub = holdValue > 0 ? $"+{UiTheme.Credits(holdValue)} CR" : "NOTHING WANTED";
        SellHold.Enabled = holdValue > 0;
        SellHold.Selected = holdValue > 0;
        SellSuit.Changed();
        SellHold.Changed();
        QueueRedraw();
        return true;
    }

    List<RowItem> Rows(Inventory inv) => inv.Stacks
        .OrderByDescending(s => _trader.IsWanted(s.Item))
        .Select(s =>
        {
            int price = _trader.PriceFor(s.Item);
            string right = price > 0 ? $"x{s.Count}  {price * s.Count,5}" : $"x{s.Count}      -";
            return new RowItem(s.Item.Name, right, C.Rule, _trader.IsWanted(s.Item), price > 0, s.Item);
        }).ToList();

    public override void _Draw()
    {
        this.Block(new Rect2(Vector2.Zero, Size), C.Glass);
        this.Header(Size.X, "TRADE LINK", $"CR {UiTheme.Credits(_wallet.Credits)}", C.Accent2);
        PageChrome.Strip(this, Size.X, _warnings, $"{_trader.Name}  -  {_message}", C.Accent2);

        this.Caption(new Vector2(24, 150), "DEPOT BUYS", C.Accent2);
        this.Caption(new Vector2(24, 476), "DEPOT SELLS  -  CLICK TO BUY", C.Good);
        this.Caption(new Vector2(448, 150), $"YOUR SUIT  {_suit.TotalVolumeL:0}/{_suit.VolumeCapL:0} L", C.Text);
        this.Caption(new Vector2(868, 150), $"SHIP HOLD  {_hold.TotalVolumeL:0}/{_hold.VolumeCapL:0} L", C.Text);
        this.Block(new Rect2(434, 160, 4, 544), C.Rule);
        this.Block(new Rect2(854, 160, 4, 544), C.Rule);
    }
}
