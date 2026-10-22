using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

[DefaultProperty("Items")]
[DefaultEvent("SelectedIndexChanged")]
public class FuzzyComboBox : ComboBox
{
    #region Fields

    private List<object> _originalItems = new List<object>();
    private string _filterText = "";
    private bool _isFiltering = false;
    private bool _suspendFilter = false;
    private bool _isFuzzyMatch = true;

    // 原生属性保留
    private ComboBoxStyle _dropDownStyle = ComboBoxStyle.DropDown;

    #endregion

    #region Properties

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public new ComboBox.ObjectCollection Items => base.Items;

    [Browsable(true)]
    [DefaultValue(ComboBoxStyle.DropDown)]
    public new ComboBoxStyle DropDownStyle
    {
        get => _dropDownStyle;
        set
        {
            _dropDownStyle = value;
            base.DropDownStyle = value;
        }
    }

    [Browsable(true)]
    [DefaultValue(false)]
    public bool FuzzyMatchEnabled
    {
        get => _isFuzzyMatch;
        set => _isFuzzyMatch = value;
    }


    [Browsable(false)]
    public new string DisplayMember
    {
        get => base.DisplayMember;
        set => base.DisplayMember = value;
    }

    [Browsable(false)]
    public new string ValueMember
    {
        get => base.ValueMember;
        set => base.ValueMember = value;
    }

    [Browsable(false)]
    public new object DataSource
    {
        get => base.DataSource;
        set => base.DataSource = value;
    }

    #endregion

    #region Constructor

    public FuzzyComboBox()
    {
        // 设置默认样式
        base.DropDownStyle = ComboBoxStyle.DropDown;
        base.AutoCompleteMode = AutoCompleteMode.None;

        // 启用绘图模式
        base.DrawMode = DrawMode.OwnerDrawFixed;
        base.IntegralHeight = false;

        // 事件绑定
        base.TextUpdate += OnTextUpdate;
        base.DrawItem += OnDrawItem;
        base.SelectionChangeCommitted += OnSelectionChangeCommitted;
        base.DropDown += OnDropDown;
        base.DropDownClosed += OnDropDownClosed;
        base.KeyDown += OnKeyDown;
        base.KeyPress += OnKeyPress;
        base.LostFocus += OnLostFocus;
        base.Validating += OnValidating;
        base.Validated += OnValidated;
        base.MouseClick += OnMouseClick;
    }

    #endregion

    #region Fuzzy Search Logic

    // 精确匹配（包含）
    private bool FuzzyMatch(string source, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        if (string.IsNullOrEmpty(source)) return false;

        return source.IndexOf(pattern, StringComparison.OrdinalIgnoreCase)>=0;
    }

    // 或者开头匹配（可选）
    // private bool FuzzyMatch(string source, string pattern)
    // {
    //     if (string.IsNullOrEmpty(pattern)) return true;
    //     if (string.IsNullOrEmpty(source)) return false;
    //     
    //     return source.StartsWith(pattern, StringComparison.OrdinalIgnoreCase);
    // }

    private int FuzzyScore(string source, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;

        source = source.ToLower();
        pattern = pattern.ToLower();

        // 开头匹配得分最高
        if (source.StartsWith(pattern))
            return 100;

        // 包含匹配按长度排序（短的排前面）
        return 1000 - source.Length;
    }

    private string GetItemText(object item)
    {
        if (item == null) return "";
        if (item is string s) return s;
        if (!string.IsNullOrEmpty(DisplayMember) && item.GetType().GetProperty(DisplayMember) != null)
            return item.GetType().GetProperty(DisplayMember)?.GetValue(item)?.ToString() ?? "";
        return item.ToString() ?? "";
    }

    private int _filterVersion = 0;

    private void ApplyFilter()
    {
        if (_suspendFilter || !_isFuzzyMatch) return;

        string userInput = base.Text;
        int cursorPos = userInput.Length;
        _filterText = userInput;

        int originalIndex = -1;
        if (base.SelectedIndex >= 0 && base.SelectedIndex < base.Items.Count)
        {
            object current = base.Items[base.SelectedIndex];
            originalIndex = _originalItems.FindIndex(item => Equals(item, current));
        }

        if (string.IsNullOrEmpty(_filterText))
        {
            if (_isFiltering || base.Items.Count != _originalItems.Count)
            {
                _isFiltering = false;
                base.Items.Clear();
                base.Items.AddRange(_originalItems.ToArray());

                if (originalIndex >= 0 && originalIndex < base.Items.Count)
                    base.SelectedIndex = originalIndex;
            }
            base.DroppedDown = false;
            return;
        }

        var matched = _originalItems
            .Where(item => FuzzyMatch(GetItemText(item), _filterText))
            .OrderByDescending(item => FuzzyScore(GetItemText(item), _filterText))
            .ThenBy(item => GetItemText(item))
            .ToList();

        _isFiltering = true;
        int thisVersion = ++_filterVersion;  // 递增版本号

        base.Items.Clear();
        if (matched.Count > 0)
        {
            base.Items.AddRange(matched.ToArray());
            base.DroppedDown = true;
        }
        else
        {
            base.DroppedDown = false;
            return;
        }

        BeginInvoke(new Action(() =>
        {
            // 只在版本一致时才执行（防止过期操作覆盖）
            if (thisVersion != _filterVersion) return;

            base.Text = userInput;
            base.Select(cursorPos, 0);
            _filterText = userInput;
        }));
    }

    private int FindOriginalIndex(object item)
    {
        if (item == null) return -1;
        for (int i = 0; i < _originalItems.Count; i++)
        {
            if (Equals(_originalItems[i], item))
                return i;
        }
        return -1;
    }

    #endregion

    #region Event Handlers

    private void OnTextUpdate(object sender, EventArgs e)
    {
        ApplyFilter();
    }

    private void OnDrawItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds);
            return;
        }

        string itemText = GetItemText(Items[e.Index]);
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool isFocused = (e.State & DrawItemState.Focus) == DrawItemState.Focus;

        // 背景
        Color backColor = isSelected ? SystemColors.Highlight : SystemColors.Window;
        Color textColor = isSelected ? SystemColors.HighlightText : SystemColors.WindowText;

        using (var brush = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        // 高亮匹配文字
        if (_isFiltering && !string.IsNullOrEmpty(_filterText))
        {
            float x = e.Bounds.X + 2;
            float y = e.Bounds.Y + (e.Bounds.Height - e.Graphics.MeasureString(itemText, Font).Height) / 2;

            int matchStart = -1;
            int matchLength = 0;

            // 查找匹配部分
            string lowerText = itemText.ToLower();
            string lowerFilter = _filterText.ToLower();

            for (int i = 0; i < lowerText.Length && matchLength < lowerFilter.Length; i++)
            {
                if (lowerText[i] == lowerFilter[matchLength])
                {
                    if (matchLength == 0) matchStart = i;
                    matchLength++;
                }
            }

            if (matchStart >= 0 && matchLength > 0)
            {
                // 绘制匹配前的文字
                if (matchStart > 0)
                {
                    string before = itemText.Substring(0, matchStart);
                    e.Graphics.DrawString(before, Font, new SolidBrush(textColor), x, y);
                    x += e.Graphics.MeasureString(before, Font).Width;
                }

                // 绘制匹配文字（高亮）
                string match = itemText.Substring(matchStart, matchLength);
                SizeF matchSize = e.Graphics.MeasureString(match, Font);
                e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(180, 255, 230, 100)),
                    x, y, matchSize.Width, matchSize.Height);
                e.Graphics.DrawString(match, Font, new SolidBrush(isSelected ? textColor : Color.DarkBlue), x, y);
                x += matchSize.Width;

                // 绘制匹配后的文字
                if (matchStart + matchLength < itemText.Length)
                {
                    string after = itemText.Substring(matchStart + matchLength);
                    e.Graphics.DrawString(after, Font, new SolidBrush(textColor), x, y);
                }
            }
            else
            {
                e.Graphics.DrawString(itemText, Font, new SolidBrush(textColor), x, y);
            }
        }
        else
        {
            float x = e.Bounds.X + 2;
            float y = e.Bounds.Y + (e.Bounds.Height - e.Graphics.MeasureString(itemText, Font).Height) / 2;
            e.Graphics.DrawString(itemText, Font, new SolidBrush(textColor), x, y);
        }

        // 焦点框
        if (isFocused && !isSelected)
        {
            using (var dottedPen = new Pen(Color.Gray) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            {
                e.Graphics.DrawRectangle(dottedPen, e.Bounds.X, e.Bounds.Y,
                    e.Bounds.Width - 1, e.Bounds.Height - 1);
            }
        }
    }

    //private void OnSelectionChangeCommitted(object sender, EventArgs e)
    //{
    //    if (SelectedIndex >= 0 && Items.Count > 0)
    //    {
    //        _isFiltering = false;
    //        _filterText = "";
    //        // 选中后更新文本为选中项
    //        base.Text = GetItemText(SelectedItem);
    //        OnSelectedIndexChanged(EventArgs.Empty);
    //    }
    //}

    private void OnSelectionChangeCommitted(object sender, EventArgs e)
    {
        if (SelectedIndex >= 0 && Items.Count > 0)
        {
            // 关键：先记录选中的是哪个原始项
            object selectedItem = Items[SelectedIndex];
            int originalIndex = _originalItems.FindIndex(item => Equals(item, selectedItem));

            ++_filterVersion;
            _isFiltering = false;
            _filterText = "";

            // 立即恢复原始列表并设置正确的选中项
            base.Items.Clear();
            base.Items.AddRange(_originalItems.ToArray());

            if (originalIndex >= 0 && originalIndex < base.Items.Count)
                base.SelectedIndex = originalIndex;

            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    private void OnDropDown(object sender, EventArgs e)
    {
        if (!_isFiltering)
        {
            // 记录原始数据
            _originalItems.Clear();
            _originalItems.AddRange(Items.Cast<object>().ToArray());
        }
    }

    private void OnDropDownClosed(object sender, EventArgs e)
    {
        if (_isFiltering)
        {
            // 用户没有点选就关闭了，恢复原始列表
            int savedIdx = -1;
            if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
            {
                object current = Items[SelectedIndex];
                savedIdx = _originalItems.FindIndex(item => Equals(item, current));
            }

            _isFiltering = false;

            base.Items.Clear();
            base.Items.AddRange(_originalItems.ToArray());

            if (savedIdx >= 0 && savedIdx < base.Items.Count)
                base.SelectedIndex = savedIdx;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && DroppedDown)
        {
            e.SuppressKeyPress = true;
        }
    }

    private void OnKeyPress(object sender, KeyPressEventArgs e)
    {
        // 允许所有按键正常处理
    }

    private void OnLostFocus(object sender, EventArgs e)
    {
        // 失焦时恢复原始列表
        if (_isFiltering)
        {
            _isFiltering = false;
            _filterText = "";

            int selectedIdx = base.SelectedIndex;
            object selected = base.SelectedItem;

            base.Items.Clear();
            base.Items.AddRange(_originalItems.ToArray());

            if (selected != null)
            {
                int idx = -1;
                for (int i = 0; i < base.Items.Count; i++)
                {
                    if (Equals(base.Items[i], selected))
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx >= 0) base.SelectedIndex = idx;
            }
        }
    }

    private void OnValidating(object sender, CancelEventArgs e)
    {
        // 验证时恢复原始数据
    }

    private void OnValidated(object sender, EventArgs e)
    {
        // 验证完成后处理
    }

    private void OnMouseClick(object sender, MouseEventArgs e)
    {
        // 确保点击时记录原始数据
        if (_originalItems.Count == 0)
        {
            _originalItems.AddRange(Items.Cast<object>().ToArray());
        }
    }

    #endregion

    #region Public Methods

    public void AddItem(object item)
    {
        _originalItems.Add(item);
        if (!_isFiltering)
            base.Items.Add(item);
    }

    public void RemoveItem(object item)
    {
        _originalItems.Remove(item);
        if (!_isFiltering)
            base.Items.Remove(item);
    }

    public void ClearItems()
    {
        _originalItems.Clear();
        base.Items.Clear();
    }

    public void LoadItems(IEnumerable items)
    {
        _originalItems.Clear();
        _originalItems.AddRange(items.Cast<object>().ToArray());

        if (!_isFiltering)
        {
            base.Items.Clear();
            base.Items.AddRange(_originalItems.ToArray());
        }
    }

    public int FindString(string text, int startIndex = 0)
    {
        return base.FindString(text, startIndex);
    }

    public int FindStringExact(string text, int startIndex = 0)
    {
        return base.FindStringExact(text, startIndex);
    }

    public new void Select(int start, int length)
    {
        base.Select(start, length);
    }

    public new void Focus()
    {
        base.Focus();
    }

    #endregion

    #region Protected Methods

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 确保初始化时记录原始数据
        if (_originalItems.Count == 0 && Items.Count > 0)
        {
            _originalItems.AddRange(Items.Cast<object>().ToArray());
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
    }

    public new string Text
    {
        get => base.Text;
        set
        {
            if (!_isFiltering)
                base.Text = value;
        }
    }

    #endregion
}