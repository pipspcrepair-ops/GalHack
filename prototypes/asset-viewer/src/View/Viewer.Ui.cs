using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AssetViewer;

/// <summary>The panels: the browser on the left; the asset, its notes, its animations, the view and the files on the right.</summary>
public partial class Viewer
{
    const float LeftWidth = 330f, RightWidth = 360f;

    CanvasLayer _layer;
    Control _root;
    PanelContainer _left, _right;
    LineEdit _search;
    Tree _tree;
    Label _counts;
    readonly Dictionary<string, TreeItem> _items = new();
    readonly List<TreeItem> _leaves = new();
    bool _loading;

    // Right panel.
    Label _title, _path, _error;
    Button _randomise;
    HBoxContainer _seedRow;
    LineEdit _seedBox;
    VBoxContainer _optionBox;
    GridContainer _factBox;
    VBoxContainer _animSection;
    ItemList _animList;
    HSlider _animSpeed;
    Label _animLabel;
    TextEdit _animComment;
    CheckBox _animLike, _animProblem;
    TextEdit _comment;
    CheckBox _like, _problem;
    Button _orbitButton, _freeButton;
    CheckBox _spinBox, _groundBox, _wireBox;
    HSlider _spinSlider, _lightSlider, _fovSlider;
    LineEdit _positionName;
    ItemList _bookmarks;
    Label _fileLabel, _saveLabel, _status, _toast, _help;
    float _toastLeft;
    bool _panelsVisible = true;

    // ---- building ---------------------------------------------------------------------------------

    void BuildUi()
    {
        _layer = new CanvasLayer { Name = "Ui" };
        AddChild(_layer);
        _root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.Theme = MakeTheme();
        _layer.AddChild(_root);

        BuildLeft();
        BuildRight();

        _status = new Label { Name = "Status", MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        _status.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _status.OffsetTop = -30f; _status.OffsetBottom = -6f;
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.85f));
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _status.AddThemeConstantOverride("outline_size", 6);
        _root.AddChild(_status);

        _toast = new Label { Name = "Toast", MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _toast.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _toast.OffsetTop = 14f; _toast.OffsetBottom = 44f;
        _toast.AddThemeColorOverride("font_color", new Color("fff2c4"));
        _toast.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        _toast.AddThemeConstantOverride("outline_size", 8);
        _toast.AddThemeFontSizeOverride("font_size", 18);
        _root.AddChild(_toast);

        _help = new Label { Name = "Help", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false, Text = HelpText };
        _help.SetAnchorsPreset(Control.LayoutPreset.Center);
        _help.GrowHorizontal = Control.GrowDirection.Both;
        _help.GrowVertical = Control.GrowDirection.Both;
        _help.AddThemeColorOverride("font_color", Colors.White);
        _help.AddThemeColorOverride("font_outline_color", Colors.Black);
        _help.AddThemeConstantOverride("outline_size", 8);
        _root.AddChild(_help);

        StyleBoxes(_root);
        Marks.StyleCheckBox(_like, Marks.LikeColor);
        Marks.StyleCheckBox(_animLike, Marks.LikeColor);
        Marks.StyleCheckBox(_problem, Marks.ProblemColor);
        Marks.StyleCheckBox(_animProblem, Marks.ProblemColor);
        NoFocusOnButtons(_root);
        PopulateTree();
        RefreshBookmarks();
        RefreshSaveLabel();
    }

    static Theme MakeTheme()
    {
        var theme = new Theme { DefaultFontSize = 15 };
        var panel = new StyleBoxFlat { BgColor = new Color(0.075f, 0.082f, 0.095f, 0.93f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 };
        panel.BorderColor = new Color(0.22f, 0.25f, 0.3f);
        panel.BorderWidthLeft = panel.BorderWidthRight = 1;
        theme.SetStylebox("panel", "PanelContainer", panel);
        return theme;
    }

    static Label Header(string text)
    {
        var l = new Label { Text = text.ToUpperInvariant() };
        l.AddThemeColorOverride("font_color", new Color("8fb4d8"));
        l.AddThemeFontSizeOverride("font_size", 13);
        return l;
    }

    static Button Btn(string text, Action pressed, string tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        b.Pressed += pressed;
        return b;
    }

    static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        foreach (Control c in children) row.AddChild(c);
        return row;
    }

    static void StyleBoxes(Node n)
    {
        if (n is CheckBox box) Marks.StyleCheckBox(box, new Color("5aa9e6"));
        foreach (Node c in n.GetChildren()) StyleBoxes(c);
    }

    static void NoFocusOnButtons(Node n)
    {
        if (n is BaseButton or Slider or OptionButton) ((Control)n).FocusMode = Control.FocusModeEnum.None;
        foreach (Node c in n.GetChildren()) NoFocusOnButtons(c);
    }

    void BuildLeft()
    {
        _left = new PanelContainer { Name = "Browser" };
        _left.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        _left.OffsetRight = LeftWidth;
        _root.AddChild(_left);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _left.AddChild(box);

        _search = new LineEdit { PlaceholderText = "Search", ClearButtonEnabled = true };
        _search.TextChanged += _ => PopulateTree();
        box.AddChild(_search);

        _tree = new Tree
        {
            HideRoot = true, Columns = 2, SizeFlagsVertical = Control.SizeFlags.ExpandFill, AllowSearch = false,
            SelectMode = Tree.SelectModeEnum.Single,
        };
        _tree.SetColumnExpand(0, true);
        _tree.SetColumnExpand(1, false);
        _tree.SetColumnCustomMinimumWidth(1, Marks.Width + 6);
        _tree.ItemSelected += () =>
        {
            if (_loading) return;
            TreeItem t = _tree.GetSelected();
            if (t == null) return;
            int index = t.GetMetadata(0).AsInt32();
            if (index >= 0) SelectSoon(Registry.Entries[index]);
        };
        box.AddChild(_tree);

        _counts = new Label();
        _counts.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        box.AddChild(_counts);
    }

    void BuildRight()
    {
        _right = new PanelContainer { Name = "Inspector" };
        _right.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _right.OffsetLeft = -RightWidth;
        _root.AddChild(_right);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _right.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(box);

        _title = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _title.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(_title);
        _path = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _path.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        box.AddChild(_path);
        _error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _error.AddThemeColorOverride("font_color", Marks.ProblemColor);
        box.AddChild(_error);

        box.AddChild(Row(Btn("< Prev", () => Step(-1), "[  or  Page Up"), Btn("Next >", () => Step(1), "]  or  Page Down")));

        // Randomise.
        _randomise = Btn("Randomise", Randomise, "R");
        _seedBox = new LineEdit { PlaceholderText = "Seed", CustomMinimumSize = new Vector2(120, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _seedBox.TextSubmitted += t => { if (ulong.TryParse(t.Trim(), out ulong s)) SetSeed(s); _seedBox.ReleaseFocus(); };
        _seedRow = Row(_randomise, new Label { Text = "Seed" }, _seedBox);
        box.AddChild(_seedRow);

        _optionBox = new VBoxContainer();
        box.AddChild(_optionBox);
        _factBox = new GridContainer { Columns = 2 };
        _factBox.AddThemeConstantOverride("h_separation", 12);
        box.AddChild(_factBox);

        // Animations.
        _animSection = new VBoxContainer();
        _animSection.AddThemeConstantOverride("separation", 6);
        box.AddChild(new HSeparator());
        _animSection.AddChild(Header("Animations"));
        _animList = new ItemList { CustomMinimumSize = new Vector2(0, 130), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AllowSearch = false, AutoHeight = false };
        _animList.ItemSelected += i =>
        {
            if (_loading) return;
            PlayAnimation(_animList.GetItemText((int)i));
        };
        _animSection.AddChild(_animList);
        _animSpeed = new HSlider { MinValue = 0, MaxValue = 3, Step = 0.05, Value = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _animSpeed.ValueChanged += v => { AnimSpeed = (float)v; Instance?.SetSpeed(AnimSpeed); };
        _animSection.AddChild(Row(new Label { Text = "Speed" }, _animSpeed, Btn("Restart", () => { if (Animation != "") Instance?.PlayAnimation(Animation); }, "Play it again from the start")));
        _animLabel = new Label { Text = "" };
        _animLabel.AddThemeColorOverride("font_color", Marks.CommentColor);
        _animSection.AddChild(_animLabel);
        _animComment = new TextEdit { PlaceholderText = "Comment on this animation", CustomMinimumSize = new Vector2(0, 70), WrapMode = TextEdit.LineWrappingMode.Boundary };
        _animComment.TextChanged += () => { if (!_loading) EditAnimationNote(n => n.Comment = _animComment.Text); };
        _animSection.AddChild(_animComment);
        _animLike = new CheckBox { Text = "Really like" };
        _animLike.Toggled += on => { if (!_loading) EditAnimationNote(n => n.Like = on); };
        _animProblem = new CheckBox { Text = "Problem" };
        _animProblem.Toggled += on => { if (!_loading) EditAnimationNote(n => n.Problem = on); };
        _animSection.AddChild(Row(_animLike, _animProblem));
        box.AddChild(_animSection);

        // Asset notes.
        box.AddChild(new HSeparator());
        box.AddChild(Header("Notes"));
        _comment = new TextEdit { PlaceholderText = "Comment on this asset", CustomMinimumSize = new Vector2(0, 110), WrapMode = TextEdit.LineWrappingMode.Boundary };
        _comment.TextChanged += () => { if (!_loading) EditNote(n => n.Comment = _comment.Text); };
        box.AddChild(_comment);
        _like = new CheckBox { Text = "Really like", TooltipText = "L" };
        _like.Toggled += on => { if (!_loading) EditNote(n => n.Like = on); };
        _problem = new CheckBox { Text = "Problem", TooltipText = "P" };
        _problem.Toggled += on => { if (!_loading) EditNote(n => n.Problem = on); };
        box.AddChild(Row(_like, _problem));

        // View.
        box.AddChild(new HSeparator());
        box.AddChild(Header("View"));
        _orbitButton = Btn("Orbit", () => SetFreeCamera(false), "C");
        _freeButton = Btn("Free camera", () => SetFreeCamera(true), "C");
        _orbitButton.ToggleMode = _freeButton.ToggleMode = true;
        box.AddChild(Row(_orbitButton, _freeButton, Btn("Frame", () => { _frameWanted = true; FrameNow(); }, "F")));
        _spinBox = new CheckBox { Text = "Turntable", ButtonPressed = true, TooltipText = "Space" };
        _spinBox.Toggled += on => Spin = on;
        _spinSlider = new HSlider { MinValue = 0.05, MaxValue = 2, Step = 0.01, Value = SpinSpeed, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _spinSlider.ValueChanged += v => SpinSpeed = (float)v;
        box.AddChild(Row(_spinBox, _spinSlider));
        _groundBox = new CheckBox { Text = "Ground", ButtonPressed = true, TooltipText = "G" };
        _groundBox.Toggled += on => { Stage.GroundWanted = on; Stage.Refresh(); };
        _wireBox = new CheckBox { Text = "Wireframe", TooltipText = "Z" };
        _wireBox.Toggled += SetWireframe;
        box.AddChild(Row(_groundBox, _wireBox));
        _lightSlider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.5, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _lightSlider.ValueChanged += v => { Stage.Light = (float)v; Stage.Refresh(); };
        box.AddChild(Row(new Label { Text = "Sun" }, _lightSlider));
        _fovSlider = new HSlider { MinValue = 20, MaxValue = 110, Step = 1, Value = 70, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fovSlider.ValueChanged += v => Cam.ApplyFov((float)v);
        box.AddChild(Row(new Label { Text = "Lens" }, _fovSlider));

        // Positions.
        box.AddChild(new HSeparator());
        box.AddChild(Header("Positions"));
        _positionName = new LineEdit { PlaceholderText = "Name (optional)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddChild(Row(_positionName, Btn("Save position", () => { SavePosition(_positionName.Text); _positionName.Text = ""; }, "F5")));
        _bookmarks = new ItemList { CustomMinimumSize = new Vector2(0, 110), AllowSearch = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bookmarks.ItemActivated += i => LoadBookmark((int)i);
        box.AddChild(_bookmarks);
        box.AddChild(Row(
            Btn("Load", () => { int[] s = _bookmarks.GetSelectedItems(); if (s.Length > 0) LoadBookmark(s[0]); else LoadLastBookmark(); }, "F9 loads the newest"),
            Btn("Delete", () => { int[] s = _bookmarks.GetSelectedItems(); if (s.Length > 0) { Notes.RemoveBookmark(Notes.Bookmarks[s[0]]); SaveNow(false); RefreshBookmarks(); } })));

        // Files.
        box.AddChild(new HSeparator());
        box.AddChild(Header("Notes file"));
        box.AddChild(Row(
            Btn("Save", () => SaveNow(true), "Ctrl+S"),
            Btn("Copy report", () => { DisplayServer.ClipboardSet(Notes.Report()); Toast("Report copied", 2f); }, "Problems, likes and comments as text"),
            Btn("Open folder", () => { System.IO.Directory.CreateDirectory(Notes.Folder); OS.ShellOpen(Notes.Folder); })));
        _saveLabel = new Label();
        _saveLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        box.AddChild(_saveLabel);
        _fileLabel = new Label { Text = Notes.NotesPath, AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        _fileLabel.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.4f));
        _fileLabel.AddThemeFontSizeOverride("font_size", 12);
        box.AddChild(_fileLabel);
    }

    const string HelpText =
        "Orbit camera\n" +
        "  left drag  turn          right / middle drag  pan          wheel  zoom\n" +
        "  W S  zoom     A D  pan sideways     Q E  pan down / up     arrows  turn     Shift  faster\n\n" +
        "Free camera (C)\n" +
        "  hold left or right mouse  look     W A S D  move     E or Space  up     Q or Ctrl  down\n" +
        "  wheel  speed     Shift  faster\n\n" +
        "R  randomise          [ ]  or  Page Up / Down  previous / next asset          , .  previous / next animation\n" +
        "Space  turntable      C  orbit / free      F  frame      G  ground      Z  wireframe      H  hide panels\n" +
        "L  really like      P  problem      F5  save position      F9  load newest position      Ctrl+S  save      F1  this help";

    // ---- the browser tree -------------------------------------------------------------------------

    void PopulateTree()
    {
        _loading = true;
        string filter = _search?.Text?.Trim() ?? "";
        _tree.Clear();
        _items.Clear();
        _leaves.Clear();
        TreeItem root = _tree.CreateItem();
        var categories = new Dictionary<string, TreeItem>();
        var groups = new Dictionary<string, TreeItem>();
        int shown = 0;
        string currentKey = Entry?.Key;
        for (int i = 0; i < Registry.Entries.Count; i++)
        {
            AssetEntry e = Registry.Entries[i];
            if (filter.Length > 0 && !Matches(e, filter)) continue;
            if (!categories.TryGetValue(e.Category, out TreeItem cat))
            {
                cat = _tree.CreateItem(root);
                cat.SetText(0, e.Category);
                cat.SetMetadata(0, -1);
                cat.SetCustomColor(0, new Color("8fb4d8"));
                cat.Collapsed = filter.Length == 0 && (Entry == null || Entry.Category != e.Category);
                categories[e.Category] = cat;
            }
            TreeItem parent = cat;
            string path = e.Category;
            if (e.Group.Length > 0)
            {
                foreach (string part in e.Group.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    path += "/" + part;
                    if (!groups.TryGetValue(path, out TreeItem g))
                    {
                        g = _tree.CreateItem(parent);
                        g.SetText(0, part);
                        g.SetMetadata(0, -1);
                        g.SetCustomColor(0, new Color(0.78f, 0.82f, 0.88f));
                        g.Collapsed = filter.Length == 0 && !(Entry != null && Entry.Category == e.Category && (Entry.Group + "/").StartsWith(path[(e.Category.Length + 1)..] + "/"));
                        groups[path] = g;
                    }
                    parent = g;
                }
            }
            TreeItem leaf = _tree.CreateItem(parent);
            leaf.SetText(0, e.Name);
            leaf.SetMetadata(0, i);
            _items[e.Key] = leaf;
            _leaves.Add(leaf);
            StyleLeaf(e, leaf);
            shown++;
        }
        // Count the leaves under each folder, for orientation.
        _counts.Text = filter.Length > 0 ? $"{shown} of {Registry.Entries.Count}" : $"{Registry.Entries.Count} assets";
        if (currentKey != null && _items.TryGetValue(currentKey, out TreeItem sel)) { sel.Select(0); _tree.ScrollToItem(sel); }
        _loading = false;
    }

    static bool Matches(AssetEntry e, string filter)
    {
        foreach (string word in filter.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!(e.Name.Contains(word, StringComparison.OrdinalIgnoreCase) || e.Group.Contains(word, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(word, StringComparison.OrdinalIgnoreCase)))
                return false;
        return true;
    }

    void StyleLeaf(AssetEntry e, TreeItem leaf)
    {
        AssetNote n = Notes.Get(e);
        bool like = n != null && (n.Like || n.Animations.Values.Any(a => a.Like));
        bool problem = n != null && (n.Problem || n.Animations.Values.Any(a => a.Problem));
        bool comment = n != null && (!string.IsNullOrWhiteSpace(n.Comment) || n.Animations.Values.Any(a => !string.IsNullOrWhiteSpace(a.Comment)));
        leaf.SetIcon(1, Marks.For(like, problem, comment));
        leaf.SetCustomColor(0, problem ? Marks.ProblemColor.Lightened(0.25f) : like ? Marks.LikeColor.Lightened(0.2f) : new Color(0.92f, 0.93f, 0.95f));
    }

    void Step(int dir)
    {
        if (_leaves.Count == 0) return;
        int at = Entry != null && _items.TryGetValue(Entry.Key, out TreeItem cur) ? _leaves.IndexOf(cur) : -1;
        TreeItem next = _leaves[(at + dir + _leaves.Count) % _leaves.Count];
        next.UncollapseTree();
        for (TreeItem p = next.GetParent(); p != null; p = p.GetParent()) p.Collapsed = false;
        _loading = true;
        next.Select(0);
        _loading = false;
        _tree.ScrollToItem(next);
        Select(Registry.Entries[next.GetMetadata(0).AsInt32()], null, null, true);
    }

    // ---- editing notes ----------------------------------------------------------------------------

    void EditNote(Action<AssetNote> edit)
    {
        if (Entry == null) return;
        AssetNote n = Notes.GetOrCreate(Entry);
        edit(n);
        Notes.Touch(Entry, Seed, Options);
        AfterNoteEdit();
    }

    void EditAnimationNote(Action<Note> edit)
    {
        if (Entry == null || Animation == "") return;
        Note n = Notes.GetOrCreateAnimation(Entry, Animation);
        edit(n);
        Notes.Touch(Entry, Seed, Options);
        AfterNoteEdit();
    }

    void AfterNoteEdit()
    {
        if (_items.TryGetValue(Entry.Key, out TreeItem leaf)) StyleLeaf(Entry, leaf);
        RefreshAnimationMarks();
        RefreshSaveLabel();
        ScheduleSave();
    }

    // ---- refreshing the right panel ---------------------------------------------------------------

    /// <summary>Fills every control for the asset on show.</summary>
    void RefreshPanels()
    {
        if (_title == null) return;
        _loading = true;
        if (Entry == null)
        {
            _title.Text = "Nothing here";
            _loading = false;
            return;
        }
        _title.Text = Entry.Name;
        _path.Text = Entry.Category + (Entry.Group.Length > 0 ? " / " + Entry.Group.Replace("/", " / ") : "");
        _error.Visible = BuildError != "";
        _error.Text = BuildError == "" ? "" : "Build failed: " + BuildError;
        _randomise.Visible = Entry.Randomisable;
        _seedRow.Visible = Entry.Randomisable;
        _seedBox.Text = Seed.ToString();

        foreach (Node c in _optionBox.GetChildren()) { _optionBox.RemoveChild(c); c.QueueFree(); }
        foreach (AssetOption o in Entry.Options)
        {
            var pick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
            foreach (string v in o.Values) pick.AddItem(v);
            int index = Array.IndexOf(o.Values, Options.GetValueOrDefault(o.Name, ""));
            pick.Selected = Mathf.Max(0, index);
            string name = o.Name;
            pick.ItemSelected += i => { if (!_loading) SetOption(name, pick.GetItemText((int)i)); };
            _optionBox.AddChild(Row(new Label { Text = o.Name, CustomMinimumSize = new Vector2(96, 0) }, pick));
        }

        foreach (Node c in _factBox.GetChildren()) { _factBox.RemoveChild(c); c.QueueFree(); }
        if (Instance != null)
            foreach (var (label, value) in Instance.Facts())
            {
                var l = new Label { Text = label };
                l.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
                _factBox.AddChild(l);
                _factBox.AddChild(new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(150, 0) });
            }

        AssetNote note = Notes.Get(Entry);
        _comment.Text = note?.Comment ?? "";
        _like.ButtonPressed = note?.Like ?? false;
        _problem.ButtonPressed = note?.Problem ?? false;

        string[] anims = Instance?.Animations.ToArray() ?? Array.Empty<string>();
        _animSection.Visible = anims.Length > 0;
        _animList.Clear();
        foreach (string a in anims) _animList.AddItem(a);
        _animList.CustomMinimumSize = new Vector2(0, Mathf.Clamp(anims.Length * 27 + 8, 60, 220));
        _loading = false;
        RefreshAnimationPanel();
        _animSpeed.Value = AnimSpeed;

        if (_items.TryGetValue(Entry.Key, out TreeItem leaf))
        {
            // Open the folders above it and keep it selected.
            for (TreeItem p = leaf.GetParent(); p != null; p = p.GetParent()) p.Collapsed = false;
            _loading = true;
            leaf.Select(0);
            _loading = false;
            _tree.ScrollToItem(leaf);
        }
        SyncCameraMode();
        RefreshSaveLabel();
    }

    void RefreshAnimationPanel()
    {
        if (_animList == null || Entry == null) return;
        _loading = true;
        string[] anims = Instance?.Animations.ToArray() ?? Array.Empty<string>();
        int index = Array.IndexOf(anims, Animation);
        if (index >= 0 && index < _animList.ItemCount) _animList.Select(index);
        Note n = Animation != "" ? Notes.GetAnimation(Entry, Animation) : null;
        _animLabel.Text = Animation != "" ? $"Animation: {Animation}" : "";
        _animComment.Text = n?.Comment ?? "";
        _animLike.ButtonPressed = n?.Like ?? false;
        _animProblem.ButtonPressed = n?.Problem ?? false;
        _animComment.Editable = Animation != "";
        _animLike.Disabled = _animProblem.Disabled = Animation == "";
        _loading = false;
        RefreshAnimationMarks();
    }

    void RefreshAnimationMarks()
    {
        if (_animList == null || Entry == null) return;
        string[] anims = Instance?.Animations.ToArray() ?? Array.Empty<string>();
        for (int i = 0; i < anims.Length && i < _animList.ItemCount; i++)
        {
            Note n = Notes.GetAnimation(Entry, anims[i]);
            _animList.SetItemIcon(i, n == null ? null : Marks.For(n.Like, n.Problem, !string.IsNullOrWhiteSpace(n.Comment)));
        }
    }

    void RefreshBookmarks()
    {
        if (_bookmarks == null) return;
        _bookmarks.Clear();
        foreach (Position p in Notes.Bookmarks) _bookmarks.AddItem(p.Name);
    }

    void LoadBookmark(int index)
    {
        if (index < 0 || index >= Notes.Bookmarks.Count) return;
        if (ApplyPosition(Notes.Bookmarks[index])) Toast($"Loaded: {Notes.Bookmarks[index].Name}", 2f);
    }

    void RefreshSaveLabel()
    {
        if (_saveLabel == null) return;
        var (l, p, c) = Notes.Counts();
        string when = Notes.LastSaved == default ? "not saved yet" : "saved " + Notes.LastSaved.ToString("HH:mm:ss");
        _saveLabel.Text = Notes.Problem != "" ? Notes.Problem : $"{l} liked, {p} problems, {c} commented; {(Notes.Dirty ? "unsaved changes" : when)}";
    }

    void UpdatePanelsEachFrame()
    {
        if (_status == null) return;
        string where = Entry == null ? "" : $"{Entry.Category} / {Entry.Name}" + (Entry.Randomisable ? $"   seed {Seed}" : "") + (Animation != "" ? $"   {Animation}" : "");
        _status.Text = where + (Cam.IsFree ? $"   free camera, speed {Cam.FlySpeed:0.0} m/s" : "") + "   F1 help";
        Cam.ScreenShift = _panelsVisible ? (LeftWidth - RightWidth) * 0.5f / Mathf.Max(1f, GetViewport().GetVisibleRect().Size.X) : 0f;
        if (Notes.Dirty && _saveLabel != null && (Engine.GetProcessFrames() & 15) == 0) RefreshSaveLabel();
    }

    // ---- camera mode, panels, toasts --------------------------------------------------------------

    public void SetFreeCamera(bool free)
    {
        Cam.SetFree(free);
        SyncCameraMode();
    }

    void SyncCameraMode()
    {
        if (_orbitButton == null) return;
        _orbitButton.SetPressedNoSignal(!Cam.IsFree);
        _freeButton.SetPressedNoSignal(Cam.IsFree);
        _fovSlider.SetValueNoSignal(Cam.Fov);
        _spinBox.SetPressedNoSignal(Spin);
        _groundBox.SetPressedNoSignal(Stage.GroundWanted);
        _lightSlider.SetValueNoSignal(Stage.Light);
        _spinSlider.SetValueNoSignal(SpinSpeed);
    }

    void SetPanelsVisible(bool on)
    {
        _panelsVisible = on;
        _left.Visible = _right.Visible = on;
    }

    bool PointerInView()
    {
        if (!_panelsVisible) return true;
        Vector2 m = GetViewport().GetMousePosition();
        return !_left.GetGlobalRect().HasPoint(m) && !_right.GetGlobalRect().HasPoint(m);
    }

    public void Toast(string text, float seconds)
    {
        if (_toast == null) return;
        _toast.Text = text;
        _toast.Visible = true;
        _toastLeft = seconds;
    }

    void UpdateToast(float dt)
    {
        if (_toast == null || !_toast.Visible) return;
        _toastLeft -= dt;
        _toast.Modulate = new Color(1, 1, 1, Mathf.Clamp(_toastLeft * 2f, 0f, 1f));
        if (_toastLeft <= 0f) _toast.Visible = false;
    }

    // ---- shortcuts --------------------------------------------------------------------------------

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey k || !k.Pressed || k.Echo) return;
        if (TextHasFocus()) return;
        if (k.CtrlPressed)
        {
            if (k.Keycode == Key.S) { SaveNow(true); GetViewport().SetInputAsHandled(); }
            return;
        }
        switch (k.Keycode)
        {
            case Key.R: Randomise(); break;
            case Key.Bracketright: case Key.Pagedown: Step(1); break;
            case Key.Bracketleft: case Key.Pageup: Step(-1); break;
            case Key.Period: StepAnimation(1); break;
            case Key.Comma: StepAnimation(-1); break;
            case Key.Space: Spin = !Spin; SyncCameraMode(); break;
            case Key.C: SetFreeCamera(!Cam.IsFree); break;
            case Key.F: _frameWanted = true; FrameNow(); break;
            case Key.G: Stage.GroundWanted = !Stage.GroundWanted; Stage.Refresh(); SyncCameraMode(); break;
            case Key.Z: _wireBox.ButtonPressed = !_wireBox.ButtonPressed; break;
            case Key.H: SetPanelsVisible(!_panelsVisible); break;
            case Key.L: _like.ButtonPressed = !_like.ButtonPressed; break;
            case Key.P: _problem.ButtonPressed = !_problem.ButtonPressed; break;
            case Key.F1: _help.Visible = !_help.Visible; break;
            case Key.F5: SavePosition(""); break;
            case Key.F9: LoadLastBookmark(); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }
}
