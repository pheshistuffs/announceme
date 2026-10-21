namespace AnnounceMe;

// Ticked templates are in the list; unticking one removes its reminder.
public class TemplatesForm : Form
{
    readonly ListView _list;

    public TemplatesForm()
    {
        Text = "Игры и шаблоны";
        Icon = TrayContext.AppIcon;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(600, 400);
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        var hint = new Label
        {
            Text = "Отметьте то, во что играете. Снимите галочку, и напоминание удалится. " +
                   "Время и текст потом можно поменять кнопкой «Изменить…». " +
                   "Время сброса указано для европейских серверов.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 48,
            Padding = new Padding(8, 8, 8, 0),
        };

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
        };
        _list.Columns.Add("Название", 190);
        _list.Columns.Add("Когда", 250);
        _list.Columns.Add("Текст", 340);

        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var t in Templates.All)
        {
            if (!groups.TryGetValue(t.Group, out var g))
            {
                g = new ListViewGroup(t.Group, t.Group);
                groups[t.Group] = g;
                _list.Groups.Add(g);
            }
            var item = new ListViewItem(t.Name, g) { Tag = t, Checked = Store.Data.Sites.Any(t.Matches) };
            item.SubItems.Add(Templates.Describe(t));
            item.SubItems.Add(t.Text);
            _list.Items.Add(item);
        }

        var ok = new Button { Text = "Готово", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        buttons.Controls.AddRange(new Control[] { cancel, ok });

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    // Adds ticked templates that aren't in the list yet and removes unticked ones.
    // Returns the entries that were removed, so the caller can clean up after them.
    public (List<SiteEntry> added, List<SiteEntry> removed) Apply()
    {
        var added = new List<SiteEntry>();
        var removed = new List<SiteEntry>();
        foreach (ListViewItem item in _list.Items)
        {
            var t = (Template)item.Tag;
            var existing = Store.Data.Sites.Where(t.Matches).ToList();
            if (item.Checked && existing.Count == 0)
            {
                var e = t.ToEntry();
                Store.Data.Sites.Add(e);
                added.Add(e);
            }
            else if (!item.Checked)
            {
                foreach (var e in existing)
                {
                    Store.Data.Sites.Remove(e);
                    removed.Add(e);
                }
            }
        }
        Store.Save();
        return (added, removed);
    }
}
