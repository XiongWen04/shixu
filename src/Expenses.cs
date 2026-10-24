using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DeskTodo
{
    public class ExpenseWindow : Window
    {
        public ExpenseWindow(AppController app, ExpenseRecord existing)
        {
            Title = existing == null ? "记一笔" : "修改支出"; Width = 480; Height = Math.Min(570, SystemParameters.WorkArea.Height - 64); MinWidth = 420; MinHeight = 460; ShowInTaskbar = false; Ui.Icon(this);
            SourceInitialized += (s, e) => NativeDesktop.DarkTitle(this, app.Settings.Theme == "dark");
            var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var cancel = Ui.Button("取消", () => Close()); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0); buttons.Children.Add(cancel);
            var fields = new StackPanel(); root.Children.Add(Ui.Scroll(fields)); fields.Children.Add(Ui.Text(Title, 24, "Text", true));
            var hint = Ui.Text("随手记录，让每一笔花销都有迹可循。", 12, "Muted"); hint.Margin = new Thickness(0, 8, 0, 0); fields.Children.Add(hint);
            var amount = new TextBox { Text = existing == null ? "" : existing.Amount.ToString("0.00", CultureInfo.InvariantCulture), MaxLength = 32, FontSize = 24, ToolTip = "例如 18.50，单位：元" };
            var date = new DatePicker { SelectedDate = existing == null ? DateTime.Today : DateTime.ParseExact(existing.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), FirstDayOfWeek = DayOfWeek.Monday };
            var category = new ComboBox { ItemsSource = ExpenseRecord.Categories, SelectedItem = existing == null ? "其他" : existing.Category };
            var note = new TextBox { Text = existing == null ? "" : existing.Note ?? "", MaxLength = 200, ToolTip = "例如：午饭、地铁、买书" };
            string[] labels = { "金额（元）", "支出日期", "支出分类", "备注（可选）" }; Control[] inputs = { amount, date, category, note };
            for (int n = 0; n < labels.Length; n++) { var label = Ui.Text(labels[n], 12, "Muted"); label.Margin = new Thickness(0, 16, 0, 6); fields.Children.Add(label); AutomationProperties.SetName(inputs[n], labels[n]); fields.Children.Add(inputs[n]); }
            var error = Ui.Text("", 12, "Danger"); error.Margin = new Thickness(0, 12, 0, 0); fields.Children.Add(error);
            amount.TextChanged += (s, e) => error.Text = ""; date.SelectedDateChanged += (s, e) => error.Text = ""; category.SelectionChanged += (s, e) => error.Text = ""; note.TextChanged += (s, e) => error.Text = "";
            var save = Ui.Button("保存支出", () =>
            {
                decimal value;
                if (!System.Text.RegularExpressions.Regex.IsMatch(amount.Text.Trim(), @"\A[0-9]{1,8}(\.[0-9]{1,2})?\z") || !Decimal.TryParse(amount.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) { error.Text = "请输入有效金额，例如 18.50，最多两位小数。"; amount.Focus(); return; }
                var record = new ExpenseRecord { Id = existing == null ? Guid.NewGuid().ToString("N") : existing.Id, Amount = value, Date = date.SelectedDate.HasValue ? Dates.Key(date.SelectedDate.Value) : null, Category = category.SelectedItem as string, Note = note.Text.Trim() };
                try { record.Validate(); } catch (Exception ex) { error.Text = ex.Message; return; }
                if (app.SaveExpense(record, existing != null)) Close();
            }, "Primary"); save.IsDefault = true; buttons.Children.Add(save);
            Loaded += (s, e) => { amount.Focus(); amount.SelectAll(); };
        }
    }
}
