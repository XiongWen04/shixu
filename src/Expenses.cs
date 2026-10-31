using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace DeskTodo
{
    public class ExpenseWindow : Window
    {
        public ExpenseWindow(AppController app, ExpenseRecord existing, DateTime? initialDate = null)
        {
            Title = existing == null ? "记一笔" : "修改支出"; Width = 480; Height = Math.Min(650, SystemParameters.WorkArea.Height - 64); MinWidth = 420; MinHeight = 460; ShowInTaskbar = false; Ui.Icon(this);
            SourceInitialized += (s, e) => NativeDesktop.DarkTitle(this, app.Settings.Theme == "dark");
            var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); buttons.Children.Add(cancel);
            var fields = new StackPanel(); root.Children.Add(Ui.Scroll(fields)); fields.Children.Add(Ui.Text(Title, 24, "Text", true));
            var hint = Ui.Text("随手记录，让每一笔花销都有迹可循。", 12, "Muted"); hint.Margin = new Thickness(0, 8, 0, 0); fields.Children.Add(hint);
            var amount = new TextBox { Text = existing == null ? "" : existing.Amount.ToString("0.00", CultureInfo.InvariantCulture), MaxLength = 32, FontSize = 24, ToolTip = "例如 18.50，单位：元" };
            var date = new DatePicker { SelectedDate = existing == null ? initialDate ?? DateTime.Today : DateTime.ParseExact(existing.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), FirstDayOfWeek = DayOfWeek.Monday };
            var category = new ComboBox { ItemsSource = ExpenseRecord.Categories, SelectedItem = existing == null ? "其他" : existing.Category };
            var note = new TextBox { Text = existing == null ? "" : existing.Note ?? "", MaxLength = 200, ToolTip = "例如：午饭、地铁、买书" };
            string[] labels = { "金额（元）", "支出日期", "支出分类", "备注（可选）" }; Control[] inputs = { amount, date, category, note };
            var mealFields = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; mealFields.Children.Add(Ui.Text("餐饮类型", 12, "Muted"));
            var mealButtons = new UniformGrid { Columns = 4, Margin = new Thickness(0, 7, 0, 0) }; mealFields.Children.Add(mealButtons);
            string selectedMeal = existing == null ? "其他" : existing.Meal ?? "其他";
            Action updateMeals = () => { foreach (Button button in mealButtons.Children) { bool chosen = (string)button.Content == selectedMeal; button.SetResourceReference(Button.BackgroundProperty, chosen ? "Soft" : "Panel"); button.SetResourceReference(Button.BorderBrushProperty, chosen ? "Accent" : "Line"); AutomationProperties.SetHelpText(button, chosen ? "已选择" : "未选择"); } };
            foreach (string meal in ExpenseRecord.Meals) { string choice = meal; var button = Ui.Button(choice, () => { selectedMeal = choice; updateMeals(); }, "Quiet"); button.BorderThickness = new Thickness(1); button.Margin = new Thickness(0, 0, 6, 0); AutomationProperties.SetName(button, "餐饮类型 " + meal); mealButtons.Children.Add(button); }
            updateMeals(); Action updateCategory = () => mealFields.Visibility = (string)category.SelectedItem == "餐饮" ? Visibility.Visible : Visibility.Collapsed;
            category.SelectionChanged += (s, e) => updateCategory(); updateCategory();
            for (int n = 0; n < labels.Length; n++) { var label = Ui.Text(labels[n], 12, "Muted"); label.Margin = new Thickness(0, 16, 0, 6); fields.Children.Add(label); AutomationProperties.SetName(inputs[n], labels[n]); fields.Children.Add(inputs[n]); if (n == 2) fields.Children.Add(mealFields); }
            var error = Ui.Text("", 12, "Danger"); error.Margin = new Thickness(0, 12, 0, 0); fields.Children.Add(error);
            amount.TextChanged += (s, e) => error.Text = ""; date.SelectedDateChanged += (s, e) => error.Text = ""; category.SelectionChanged += (s, e) => error.Text = ""; note.TextChanged += (s, e) => error.Text = "";
            var save = Ui.Button("保存支出", () =>
            {
                decimal value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(amount.Text.Trim(), @"\A[0-9]{1,8}(\.[0-9]{1,2})?\z") || !Decimal.TryParse(amount.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) { error.Text = "请输入有效金额，例如 18.50，最多两位小数。"; amount.Focus(); return; }
                var record = new ExpenseRecord { Id = existing == null ? Guid.NewGuid().ToString("N") : existing.Id, Amount = value, Date = date.SelectedDate.HasValue ? Dates.Key(date.SelectedDate.Value) : null, Category = category.SelectedItem as string, Meal = (string)category.SelectedItem == "餐饮" ? selectedMeal : null, Note = note.Text.Trim() };
                try { record.Validate(); } catch (Exception ex) { error.Text = ex.Message; return; }
                if (app.SaveExpense(record, existing != null)) Close();
            }, "Primary"); save.IsDefault = true; buttons.Children.Add(save);
            Loaded += (s, e) => { amount.Focus(); amount.SelectAll(); };
        }
    }

    public class ExpensePanel : ScrollViewer
    {
        readonly AppController app;
        readonly StackPanel root = new StackPanel();
        DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        public DateTime SelectedDate { get; private set; }
        bool compact, wholeMonth;
        static readonly string[] categoryColors = { null, "#7CA4B7", "#BD956D", "#9C92B9", "#B9A155", "#A5AEA0" };
        public ExpensePanel(AppController controller)
        {
            app = controller; SelectedDate = DateTime.Today; Content = root; VerticalScrollBarVisibility = ScrollBarVisibility.Auto; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            SizeChanged += (s, e) => { bool value = ActualWidth < 830; if (compact != value) { compact = value; Refresh(); } };
        }
        static Brush Color(int index) { return index == 0 ? (Brush)Application.Current.Resources["Accent"] : new SolidColorBrush((Color)ColorConverter.ConvertFromString(categoryColors[index])); }
        static string Money(decimal amount) { return "¥" + amount.ToString("0.00"); }
        static Border Card(UIElement child) { var card = Ui.Card(child, new Thickness(20)); card.Margin = new Thickness(0, 0, 0, 16); return card; }
        static TextBlock Hint(string text) { return Ui.Text(text, 11, "Muted"); }
        static Border Metric(string title, decimal value, string hint)
        {
            var panel = new StackPanel(); panel.Children.Add(Hint(title)); var text = Ui.Text(Money(value), 32, "Accent", true); text.FontFamily = new FontFamily("Segoe UI"); text.ToolTip = text.Text;
            panel.Children.Add(new Viewbox { Child = text, Height = 44, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left }); panel.Children.Add(Hint(hint)); return Card(panel);
        }
        Grid Columns(UIElement first, UIElement second, double ratio = 1, bool stack = false)
        {
            var grid = new Grid();
            if (compact || stack) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(second, 1); }
            else { grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(second, 1); ((FrameworkElement)first).Margin = new Thickness(0, 0, 16, 16); }
            grid.Children.Add(first); grid.Children.Add(second); return grid;
        }
        void ChangeMonth(DateTime date) { month = new DateTime(date.Year, date.Month, 1); SelectedDate = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month ? DateTime.Today : month; Refresh(); }
        public void Refresh()
        {
            root.Children.Clear(); var all = app.Store.State.Expenses; var summary = ExpenseSummary.Create(all, month, DateTime.Today);
            var balance = new StackPanel(); balance.Children.Add(Ui.Text("现有余额", 15, "Text", true));
            var balanceValue = Ui.Text(app.Store.State.ExpenseBalance.HasValue ? Money(app.Store.State.ExpenseBalance.Value) : "尚未设置", 32, "Accent", true);
            AutomationProperties.SetName(balanceValue, "现有余额"); balanceValue.ToolTip = balanceValue.Text;
            balance.Children.Add(new Viewbox { Child = balanceValue, Height = 48, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
            balance.Children.Add(Hint("填写当前实际余额，已有账单不重复扣减；之后新增扣减、修改按差额调整、删除返还。"));
            var balanceForm = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var balanceInput = new TextBox { Text = app.Store.State.ExpenseBalance.HasValue ? app.Store.State.ExpenseBalance.Value.ToString("0.00", CultureInfo.InvariantCulture) : "", MaxLength = 20, MinWidth = 120, ToolTip = "输入当前实际余额（元），可输入负数" };
            AutomationProperties.SetName(balanceInput, "设置现有余额（元）");
            var balanceError = Ui.Text("", 12, "Danger");
            var balanceSave = Ui.Button("设置余额", () => {
                decimal value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(balanceInput.Text.Trim(), @"\A-?[0-9]{1,12}(\.[0-9]{1,2})?\z") || !Decimal.TryParse(balanceInput.Text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) { balanceError.Text = "请输入有效余额，最多两位小数。"; return; }
                app.SetExpenseBalance(value);
            }, "Quiet"); DockPanel.SetDock(balanceSave, Dock.Right); balanceForm.Children.Add(balanceSave); balanceForm.Children.Add(balanceInput); balance.Children.Add(balanceForm); balance.Children.Add(balanceError); root.Children.Add(Card(balance));
            var overview = new UniformGrid { Columns = 3 };
            var current = Metric("所选月份支出", summary.Total, month.ToString("yyyy 年 M 月")); current.Margin = new Thickness(0, 0, 14, 16); overview.Children.Add(current);
            var today = Metric("今日支出", all.Where(r => r.Date == Dates.Key(DateTime.Today)).Sum(r => r.Amount), DateTime.Today.ToString("M 月 d 日")); today.Margin = new Thickness(0, 0, 14, 16); overview.Children.Add(today);
            overview.Children.Add(Metric("日均支出", summary.DailyAverage, "按 " + summary.AverageDays + " 天计算，含未记账日")); root.Children.Add(overview);
            var trend = new StackPanel(); var trendTop = new DockPanel(); var range = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(range, Dock.Right); trendTop.Children.Add(range);
            foreach (bool monthly in new[] { false, true }) { bool chosen = monthly; var button = Ui.Button(monthly ? "整月" : "近 7 天", () => { wholeMonth = chosen; Refresh(); }, "Quiet"); if (wholeMonth == chosen) button.SetResourceReference(Button.BackgroundProperty, "Soft"); range.Children.Add(button); }
            trendTop.Children.Add(Ui.Text("支出趋势", 15, "Text", true)); trend.Children.Add(trendTop);
            DateTime last = new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
            DateTime through = month.Year == DateTime.Today.Year && month.Month == DateTime.Today.Month ? DateTime.Today : last;
            DateTime from = wholeMonth ? month : through.AddDays(-Math.Min(6, through.Day - 1)); if (wholeMonth) through = last;
            var values = Enumerable.Range(0, (through - from).Days + 1).Select(n => new KeyValuePair<string, decimal>(Dates.Key(from.AddDays(n)), summary.ByDay[Dates.Key(from.AddDays(n))])).ToList();
            trend.Children.Add(new ExpenseChart(values, false) { Height = 160, Margin = new Thickness(0, 14, 0, 0) });
            var composition = new StackPanel(); composition.Children.Add(Ui.Text("支出构成", 15, "Text", true)); var distribution = new Grid { Margin = new Thickness(0, 15, 0, 0) }; distribution.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(142) }); distribution.ColumnDefinitions.Add(new ColumnDefinition());
            distribution.Children.Add(new ExpenseChart(summary.ByCategory.ToList(), true) { Width = 132, Height = 150 }); var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            for (int n = 0; n < ExpenseRecord.Categories.Length; n++)
            {
                string category = ExpenseRecord.Categories[n]; decimal value = summary.ByCategory[category]; var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), ToolTip = category + " " + Money(value) }; var percent = Hint((summary.Total == 0 ? 0 : value / summary.Total * 100).ToString("0.#") + "%"); DockPanel.SetDock(percent, Dock.Right); row.Children.Add(percent);
                var dot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Color(n), Margin = new Thickness(0, 0, 7, 0) }; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot); row.Children.Add(Hint(category)); legend.Children.Add(row);
            }
            Grid.SetColumn(legend, 1); distribution.Children.Add(legend); composition.Children.Add(distribution); root.Children.Add(Columns(Card(trend), Card(composition), 1.15));
            var meals = new StackPanel(); var mealHeading = new DockPanel(); var mealTotal = Hint("所选月份 " + Money(summary.ByCategory["餐饮"])); DockPanel.SetDock(mealTotal, Dock.Right); mealHeading.Children.Add(mealTotal); mealHeading.Children.Add(Ui.Text("餐饮细分", 15, "Text", true)); meals.Children.Add(mealHeading);
            var mealGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 16, 0, 0) };
            foreach (string meal in ExpenseRecord.Meals)
            {
                decimal value = summary.ByMeal[meal]; var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; panel.Children.Add(Hint(meal)); var number = Ui.Text(Money(value), 20, "Text", true); number.ToolTip = number.Text;
                panel.Children.Add(new Viewbox { Child = number, Height = 32, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left });
                var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = summary.ByCategory["餐饮"] == 0 ? 0 : (double)(value / summary.ByCategory["餐饮"] * 100), Height = 4, Margin = new Thickness(0, 8, 0, 0), BorderThickness = new Thickness(0) }; progress.SetResourceReference(ProgressBar.ForegroundProperty, "Accent"); progress.SetResourceReference(ProgressBar.BackgroundProperty, "Soft"); AutomationProperties.SetName(progress, meal + "支出 " + Money(value)); panel.Children.Add(progress); mealGrid.Children.Add(panel);
            }
            meals.Children.Add(mealGrid); root.Children.Add(Card(meals));
            var calendar = new StackPanel(); var bar = new DockPanel(); var monthButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(monthButtons, Dock.Right); bar.Children.Add(monthButtons);
            var previous = Ui.Button("‹", () => ChangeMonth(month.AddMonths(-1)), "Quiet"); previous.IsEnabled = month.Year > 1 || month.Month > 1; AutomationProperties.SetName(previous, "账单上个月"); previous.ToolTip = "上个月"; monthButtons.Children.Add(previous);
            monthButtons.Children.Add(Ui.Button("本月", () => ChangeMonth(DateTime.Today), "Quiet")); var next = Ui.Button("›", () => ChangeMonth(month.AddMonths(1)), "Quiet"); next.IsEnabled = month.Year < 9999 || month.Month < 12; next.ToolTip = "下个月"; AutomationProperties.SetName(next, "账单下个月"); monthButtons.Children.Add(next);
            bar.Children.Add(Ui.Text(month.ToString("yyyy年M月"), 18, "Text", true)); calendar.Children.Add(bar);
            if (summary.Records.Count == 0) { var empty = Hint("这个月还没有支出记录"); empty.Margin = new Thickness(0, 10, 0, 0); calendar.Children.Add(empty); }
            var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 16, 0, 6) }; foreach (string name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var text = Hint(name); text.TextAlignment = TextAlignment.Center; weekdays.Children.Add(text); } calendar.Children.Add(weekdays);
            int offset = ((int)month.DayOfWeek + 6) % 7, count = DateTime.DaysInMonth(month.Year, month.Month), cells = ((offset + count + 6) / 7) * 7;
            var days = new UniformGrid { Columns = 7, Rows = cells / 7 }; AutomationProperties.SetName(days, "支出月历");
            for (int n = 0; n < cells; n++)
            {
                int day = n - offset + 1; if (day < 1 || day > count) { days.Children.Add(new Border()); continue; }
                DateTime date = month.AddDays(day - 1); string key = Dates.Key(date); decimal value = summary.ByDay[key]; bool active = date == SelectedDate;
                var content = new StackPanel(); var number = Ui.Text(day.ToString(), 13, active ? "Accent" : "Text", active || date == DateTime.Today); number.TextAlignment = TextAlignment.Center; content.Children.Add(number);
                var amount = Ui.Text(value == 0 ? "—" : value.ToString("0.00"), 11, active ? "Accent" : "Muted"); amount.TextAlignment = TextAlignment.Center; amount.TextWrapping = TextWrapping.NoWrap; content.Children.Add(new Viewbox { Child = amount, Height = 17, Margin = new Thickness(0, 5, 0, 0), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
                var button = Ui.Button("", () => { SelectedDate = date; Refresh(); }, "Quiet"); button.Content = content; button.Height = 62; button.MinWidth = 0; button.Margin = new Thickness(2); button.Padding = new Thickness(2, 6, 2, 6); button.BorderThickness = new Thickness(active ? 1 : 0); button.SetResourceReference(Button.BackgroundProperty, active ? "Soft" : "MainCanvas"); button.SetResourceReference(Button.BorderBrushProperty, active ? "Accent" : "Line"); button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.ToolTip = key + " · " + Money(value); AutomationProperties.SetName(button, "支出日期 " + key); days.Children.Add(button);
            }
            calendar.Children.Add(days); var calendarHint = Hint("每日支出（元） · 点击日期查看全部明细"); calendarHint.Margin = new Thickness(0, 12, 0, 0); calendar.Children.Add(calendarHint);
            var details = new StackPanel(); details.Children.Add(Ui.Text(SelectedDate.ToString("M月d日 dddd"), 18, "Text", true)); string selectedKey = Dates.Key(SelectedDate); var records = summary.Records.Where(e => e.Date == selectedKey).Reverse().ToList(); var subtotal = Hint(selectedKey + " · 合计 " + Money(records.Sum(e => e.Amount))); subtotal.Margin = new Thickness(0, 7, 0, 15); details.Children.Add(subtotal);
            if (records.Count == 0) Ui.Empty(details, "这一天还没有支出记录", "点击“记一笔”，记录这一天的花销");
            foreach (var record in records)
            {
                var row = new DockPanel { Margin = new Thickness(0, 12, 0, 12) }; var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
                var amount = Ui.Text(Money(record.Amount), 18, "Accent", true); amount.HorizontalAlignment = HorizontalAlignment.Right; right.Children.Add(amount); var actions = new StackPanel { Orientation = Orientation.Horizontal }; actions.Children.Add(Ui.Button("修改", () => app.EditExpense(record), "Quiet")); actions.Children.Add(Ui.Button("删除", () => app.DeleteExpense(record), "Quiet")); right.Children.Add(actions);
                var name = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; name.Children.Add(Ui.Text(record.Category + (record.Category == "餐饮" ? " · " + (record.Meal ?? "其他") : ""), 14, "Text", true)); var note = Hint(String.IsNullOrEmpty(record.Note) ? "日常支出" : record.Note); note.TextWrapping = TextWrapping.Wrap; name.Children.Add(note); row.Children.Add(name); var line = new Border { Child = row, BorderThickness = new Thickness(0, 0, 0, 1) }; line.SetResourceReference(Border.BorderBrushProperty, "Line"); details.Children.Add(line);
            }
            root.Children.Add(Columns(Card(calendar), Card(details), 1.35, summary.ByDay.Values.Any(value => value >= 1000000m)));
        }
        internal static Brush CategoryBrush(int index) { return Color(index); }
    }

    public class ExpenseChart : FrameworkElement
    {
        readonly List<KeyValuePair<string, decimal>> values;
        readonly bool ring;
        protected override AutomationPeer OnCreateAutomationPeer() { return new FrameworkElementAutomationPeer(this); }
        public ExpenseChart(List<KeyValuePair<string, decimal>> data, bool donut)
        {
            values = data; ring = donut;
            AutomationProperties.SetName(this, (ring ? "支出分类环图" : "每日支出趋势图") + "：" + String.Join("；", values.Select(v => v.Key + " " + v.Value.ToString("0.00") + "元")));
            MouseMove += (s, e) => { if (!ring && values.Count > 0) { double width = Math.Max(1, ActualWidth - 73); int n = (int)Math.Round((e.GetPosition(this).X - 56) / width * Math.Max(1, values.Count - 1)); n = Math.Max(0, Math.Min(values.Count - 1, n)); ToolTip = values[n].Key + " · ¥" + values[n].Value.ToString("0.00"); } };
        }
        static Brush Brush(string key) { return (Brush)Application.Current.Resources[key]; }
        void Text(DrawingContext dc, string text, double x, double y, double size, string color)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); if (ActualWidth < 70 || ActualHeight < 70 || values.Count == 0) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (ring)
            {
                decimal total = values.Sum(v => v.Value); double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 13; var center = new Point(ActualWidth / 2, ActualHeight / 2);
                dc.DrawEllipse(null, new Pen(Brush("Soft"), 17), center, radius, radius); double angle = -Math.PI / 2;
                for (int n = 0; n < values.Count; n++)
                {
                    if (total == 0 || values[n].Value == 0) continue; double sweep = (double)(values[n].Value / total) * Math.PI * 2;
                    if (values[n].Value == total) dc.DrawEllipse(null, new Pen(ExpensePanel.CategoryBrush(n), 17), center, radius, radius);
                    else
                    {
                        var geometry = new StreamGeometry(); using (var context = geometry.Open()) { context.BeginFigure(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), false, false); context.ArcTo(new Point(center.X + radius * Math.Cos(angle + sweep), center.Y + radius * Math.Sin(angle + sweep)), new Size(radius, radius), 0, sweep > Math.PI, SweepDirection.Clockwise, true, false); }
                        dc.DrawGeometry(null, new Pen(ExpensePanel.CategoryBrush(n), 17), geometry);
                    }
                    angle += sweep;
                }
                Text(dc, total == 0 ? "暂无支出" : "餐饮占比", center.X - 25, center.Y - 20, 12, "Muted"); string percent = total == 0 ? "—" : (values[0].Value / total * 100).ToString("0.#") + "%"; Text(dc, percent, center.X - percent.Length * 6, center.Y, 23, "Text");
                ToolTip = String.Join("\n", values.Select(v => v.Key + " ¥" + v.Value.ToString("0.00"))); return;
            }
            double maximum = Math.Max(.01, (double)values.Max(v => v.Value)); double magnitude = Math.Pow(10, Math.Floor(Math.Log10(maximum))); maximum = Math.Ceiling(maximum / magnitude) * magnitude;
            double left = 56, right = 17, top = 26, bottom = ActualHeight - 29, plotWidth = Math.Max(1, ActualWidth - left - right);
            Text(dc, "元", 0, 0, 11, "Muted");
            for (int n = 0; n < 3; n++) { double value = maximum * n / 2, y = bottom - (bottom - top) * n / 2; dc.DrawLine(new Pen(Brush("Line"), 1), new Point(left, y), new Point(ActualWidth - right, y)); string label = value >= 100000000 ? (value / 100000000).ToString("0.#") + "亿" : value >= 10000 ? (value / 10000).ToString("0.#") + "万" : value.ToString("0.##"); Text(dc, label, 0, y - 7, 11, "Muted"); }
            var points = values.Select((v, i) => new Point(left + plotWidth * i / Math.Max(1, values.Count - 1), bottom - (double)v.Value / maximum * (bottom - top))).ToList();
            var area = new StreamGeometry(); using (var context = area.Open()) { context.BeginFigure(new Point(left, bottom), true, true); context.PolyLineTo(points, true, false); context.LineTo(new Point(points.Last().X, bottom), true, false); }
            var fill = Brush("Accent").Clone(); fill.Opacity = .12; dc.DrawGeometry(fill, null, area);
            for (int n = 1; n < points.Count; n++) dc.DrawLine(new Pen(Brush("Accent"), 2), points[n - 1], points[n]);
            foreach (var point in points) dc.DrawEllipse(Brush("Accent"), null, point, 2.7, 2.7);
            foreach (int n in new[] { 0, (values.Count - 1) / 2, values.Count - 1 }.Distinct()) { string label = DateTime.ParseExact(values[n].Key, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("M/d"); Text(dc, label, n == values.Count - 1 ? points[n].X - 25 : points[n].X, bottom + 10, 11, "Muted"); }
        }
    }
}
