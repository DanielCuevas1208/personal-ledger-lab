using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PersonalLedgerLab.App.ViewModels;
using PersonalLedgerLab.Core;

namespace PersonalLedgerLab.App;

/// <summary>
/// Main window built in code. The application avoids the Avalonia
/// XAML source generator, so the layout is constructed here in C#.
/// </summary>
public class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        Title = "Personal Ledger Lab";
        Width = 1120;
        Height = 740;
        FontFamily = new FontFamily("Segoe UI, Inter");
        Content = BuildRoot();
    }

    private Control BuildRoot()
    {
        var header = new StackPanel { Margin = new Thickness(16, 12, 16, 4) };
        header.Children.Add(new TextBlock { Text = "Personal Ledger Lab", FontSize = 20, FontWeight = FontWeight.Bold });
        var headerStatus = new TextBlock { Margin = new Thickness(0, 4, 0, 0), Opacity = 0.7 };
        BindTo(headerStatus, TextBlock.TextProperty, nameof(_vm.Status));
        header.Children.Add(headerStatus);

        var footer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x24, 0x30)),
            Padding = new Thickness(16, 8),
            Child = new TextBlock { Foreground = Brushes.White, Opacity = 0.95 }
        };
        var footerText = (TextBlock)((Border)footer).Child!;
        BindTo(footerText, TextBlock.TextProperty, nameof(_vm.Status));

        var tabs = new TabControl { Margin = new Thickness(16, 4, 16, 16) };
        tabs.Items.Add(new TabItem { Header = "Overview", Content = BuildOverview() });
        tabs.Items.Add(new TabItem { Header = "Import", Content = BuildImport() });
        tabs.Items.Add(new TabItem { Header = "Categorization rules", Content = BuildRules() });
        tabs.Items.Add(new TabItem { Header = "Budgets", Content = BuildBudgets() });
        tabs.Items.Add(new TabItem { Header = "Monthly summary", Content = BuildSummary() });

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(header, 0);
        Grid.SetRow(tabs, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(header);
        root.Children.Add(tabs);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildOverview()
    {
        var netWorthLabel = new TextBlock { Text = "Net worth", Opacity = 0.65 };
        var netWorth = new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold };
        BindTo(netWorth, TextBlock.TextProperty, nameof(_vm.NetWorthLabel));
        var detail = new TextBlock { Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        BindTo(detail, TextBlock.TextProperty, nameof(_vm.Detail));
        var topStack = new StackPanel { Spacing = 6 };
        topStack.Children.Add(netWorthLabel);
        topStack.Children.Add(netWorth);
        topStack.Children.Add(detail);

        var monthTitle = new TextBlock { Text = "Latest month summary", Opacity = 0.65 };
        var month = new TextBlock { FontSize = 18, FontWeight = FontWeight.Bold };
        BindTo(month, TextBlock.TextProperty, nameof(_vm.LatestMonthLabel));

        var incomeLabel = new TextBlock { Text = "Income", Opacity = 0.65 };
        var income = new TextBlock();
        BindTo(income, TextBlock.TextProperty, nameof(_vm.LatestMonthIncomeLabel));
        var expenseLabel = new TextBlock { Text = "Expense", Opacity = 0.65 };
        var expense = new TextBlock();
        BindTo(expense, TextBlock.TextProperty, nameof(_vm.LatestMonthExpenseLabel));

        var left = new StackPanel { Spacing = 2 };
        left.Children.Add(incomeLabel);
        left.Children.Add(income);
        Grid.SetColumn(left, 0);
        var right = new StackPanel { Spacing = 2 };
        right.Children.Add(expenseLabel);
        right.Children.Add(expense);
        Grid.SetColumn(right, 1);
        var monthGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        monthGrid.Children.Add(left);
        monthGrid.Children.Add(right);

        var monthStack = new StackPanel { Spacing = 6 };
        monthStack.Children.Add(monthTitle);
        monthStack.Children.Add(month);
        monthStack.Children.Add(monthGrid);

        var cards = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 12, 0) };
        cards.Children.Add(Card(topStack));
        cards.Children.Add(Card(monthStack));

        var accountsGrid = NewGrid(nameof(_vm.Accounts));
        Grid.SetColumn(accountsGrid, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            Margin = new Thickness(8),
            Children = { cards, accountsGrid }
        };
    }

    private Control BuildImport()
    {
        var bankLabel = new TextBlock { Text = "Bank account:", VerticalAlignment = VerticalAlignment.Center, Width = 90 };
        var bankCombo = new ComboBox { Width = 260 };
        bankCombo.ItemTemplate = new FuncDataTemplate<Account>((a, _) => new TextBlock { Text = a?.Code });
        bankCombo.ItemsSource = _vm.AssetAccounts;
        BindTo(bankCombo, SelectingItemsControl.SelectedItemProperty, nameof(_vm.SelectedAssetAccount));
        var formatLabel = new TextBlock { Text = "Format:", VerticalAlignment = VerticalAlignment.Center, Width = 52 };
        var formatCombo = new ComboBox { Width = 120 };
        formatCombo.ItemsSource = _vm.ImportFormats.ToList();
        BindTo(formatCombo, SelectingItemsControl.SelectedItemProperty, nameof(_vm.ImportFormat));
        var fileLabel = new TextBlock { Text = "Statement file:", VerticalAlignment = VerticalAlignment.Center, Width = 90 };
        var fileBox = new TextBox { Width = 420 };
        BindTo(fileBox, TextBox.TextProperty, nameof(_vm.ImportFilePath));
        var browse = new Button { Content = "Browse..." };
        browse.Click += BrowseImport_Click;
        var importBtn = new Button { Content = "Import", Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)), Foreground = Brushes.White };
        BindTo(importBtn, Button.CommandProperty, nameof(_vm.ImportCommand));
        var clearBtn = new Button { Content = "Clear" };
        BindTo(clearBtn, Button.CommandProperty, nameof(_vm.ClearImportPathCommand));

        var txGrid = NewGrid(nameof(_vm.Transactions));
        var exportLedger = new Button { Content = "Export ledger CSV" };
        BindTo(exportLedger, Button.CommandProperty, nameof(_vm.ExportLedgerCommand));
        var exportSummary = new Button { Content = "Export summary CSV" };
        BindTo(exportSummary, Button.CommandProperty, nameof(_vm.ExportSummaryCommand));

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(8) };
        var row0 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        row0.Children.Add(bankLabel);
        row0.Children.Add(bankCombo);
        row0.Children.Add(formatLabel);
        row0.Children.Add(formatCombo);
        Grid.SetRow(row0, 0);
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        row1.Children.Add(fileLabel);
        row1.Children.Add(fileBox);
        row1.Children.Add(browse);
        row1.Children.Add(importBtn);
        row1.Children.Add(clearBtn);
        Grid.SetRow(row1, 1);
        Grid.SetRow(txGrid, 2);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(exportLedger);
        actions.Children.Add(exportSummary);
        Grid.SetRow(actions, 3);
        grid.Children.Add(row0);
        grid.Children.Add(row1);
        grid.Children.Add(txGrid);
        grid.Children.Add(actions);
        return grid;
    }

    private Control BuildRules()
    {
        var editor = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 12, 0) };
        editor.Children.Add(new TextBlock { Text = "Rule editor", FontWeight = FontWeight.Bold });

        var name = new TextBox { Watermark = "Rule name" };
        BindTo(name, TextBox.TextProperty, nameof(_vm.EditorName));
        editor.Children.Add(name);

        var priorityLabel = new TextBlock { Text = "Priority", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var priority = new NumericUpDown { Minimum = 0, Maximum = 99999 };
        BindTo(priority, NumericUpDown.ValueProperty, nameof(_vm.EditorPriority));
        var priorityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        priorityRow.Children.Add(priorityLabel);
        priorityRow.Children.Add(priority);
        editor.Children.Add(priorityRow);

        var matchLabel = new TextBlock { Text = "Match", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var matchCombo = new ComboBox();
        matchCombo.ItemsSource = _vm.MatchTypes.ToList();
        BindTo(matchCombo, SelectingItemsControl.SelectedItemProperty, nameof(_vm.EditorMatchType));
        var matchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        matchRow.Children.Add(matchLabel);
        matchRow.Children.Add(matchCombo);
        editor.Children.Add(matchRow);

        var matchValue = new TextBox { Watermark = "Match value (text or regex)" };
        BindTo(matchValue, TextBox.TextProperty, nameof(_vm.EditorMatchValue));
        editor.Children.Add(matchValue);

        var categoryLabel = new TextBlock { Text = "Category", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var categoryCombo = new ComboBox { Width = 320 };
        categoryCombo.ItemTemplate = new FuncDataTemplate<Account>((a, _) => new TextBlock { Text = a?.Name });
        categoryCombo.ItemsSource = _vm.CategoryAccounts;
        BindTo(categoryCombo, SelectingItemsControl.SelectedItemProperty, nameof(_vm.EditorCategoryAccount));
        var categoryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        categoryRow.Children.Add(categoryLabel);
        categoryRow.Children.Add(categoryCombo);
        editor.Children.Add(categoryRow);

        var saveBtn = new Button { Content = "Save rule", Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)), Foreground = Brushes.White };
        BindTo(saveBtn, Button.CommandProperty, nameof(_vm.SaveRuleCommand));
        var delBtn = new Button { Content = "Delete" };
        BindTo(delBtn, Button.CommandProperty, nameof(_vm.DeleteRuleCommand));
        var newBtn = new Button { Content = "New" };
        newBtn.Click += (_, _) => _vm.ResetEditor();
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionsRow.Children.Add(saveBtn);
        actionsRow.Children.Add(delBtn);
        actionsRow.Children.Add(newBtn);
        editor.Children.Add(actionsRow);

        var prevSection = new StackPanel { Spacing = 4 };
        prevSection.Children.Add(new TextBlock { Text = "Rule preview", FontWeight = FontWeight.Bold });
        var prevDesc = new TextBox { Watermark = "Description" };
        BindTo(prevDesc, TextBox.TextProperty, nameof(_vm.PreviewDescription));
        var prevRef = new TextBox { Watermark = "Reference (optional)" };
        BindTo(prevRef, TextBox.TextProperty, nameof(_vm.PreviewReference));
        var testBtn = new Button { Content = "Test", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        BindTo(testBtn, Button.CommandProperty, nameof(_vm.PreviewCommand));
        var prevResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        BindTo(prevResult, TextBlock.TextProperty, nameof(_vm.PreviewResult));
        prevSection.Children.Add(prevDesc);
        prevSection.Children.Add(prevRef);
        prevSection.Children.Add(testBtn);
        prevSection.Children.Add(prevResult);
        editor.Children.Add(Card(prevSection));

        var rulesGrid = NewGrid(nameof(_vm.Rules));
        rulesGrid.DoubleTapped += RulesGrid_DoubleTapped;
        Grid.SetColumn(rulesGrid, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            Margin = new Thickness(8),
            Children = { editor, rulesGrid }
        };
    }

    private Control BuildSummary()
    {
        var intro = new TextBlock
        {
            Text = "Monthly totals per account. Positive net is a debit-normal result; negative is a credit-normal result.",
            Opacity = 0.7
        };
        var grid = NewGrid(nameof(_vm.Summary));
        var wrapper = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(8) };
        Grid.SetRow(intro, 0);
        Grid.SetRow(grid, 1);
        wrapper.Children.Add(intro);
        wrapper.Children.Add(grid);
        return wrapper;
    }

    private Control BuildBudgets()
    {
        var editor = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 12, 0) };
        editor.Children.Add(new TextBlock { Text = "Budget editor", FontWeight = FontWeight.Bold });

        var name = new TextBox { Watermark = "Budget name" };
        BindTo(name, TextBox.TextProperty, nameof(_vm.BudgetName));
        editor.Children.Add(name);

        var yearLabel = new TextBlock { Text = "Year", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var year = new NumericUpDown { Minimum = 0, Maximum = 9999, FormatString = "0" };
        BindTo(year, NumericUpDown.ValueProperty, nameof(_vm.BudgetYear));
        var monthLabel = new TextBlock { Text = "Month", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var month = new NumericUpDown { Minimum = 0, Maximum = 12, FormatString = "0" };
        BindTo(month, NumericUpDown.ValueProperty, nameof(_vm.BudgetMonth));
        var scopeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        scopeRow.Children.Add(yearLabel);
        scopeRow.Children.Add(year);
        scopeRow.Children.Add(monthLabel);
        scopeRow.Children.Add(month);
        editor.Children.Add(scopeRow);

        var hint = new TextBlock
        {
            Text = "Use year 0 and month 0 for a recurring budget.",
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        };
        editor.Children.Add(hint);

        var categoryLabel = new TextBlock { Text = "Category", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var categoryCombo = new ComboBox { Width = 280 };
        categoryCombo.ItemTemplate = new FuncDataTemplate<Account>((a, _) => new TextBlock { Text = a?.Name });
        categoryCombo.ItemsSource = _vm.CategoryAccounts;
        BindTo(categoryCombo, SelectingItemsControl.SelectedItemProperty, nameof(_vm.BudgetCategoryAccount));
        var categoryRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        categoryRow.Children.Add(categoryLabel);
        categoryRow.Children.Add(categoryCombo);
        editor.Children.Add(categoryRow);

        var limitLabel = new TextBlock { Text = "Limit", VerticalAlignment = VerticalAlignment.Center, Width = 60 };
        var limit = new NumericUpDown { Minimum = 0, Maximum = 1000000000, FormatString = "0.00" };
        BindTo(limit, NumericUpDown.ValueProperty, nameof(_vm.BudgetLimitMajor));
        var limitDisplay = new TextBlock { Opacity = 0.7 };
        BindTo(limitDisplay, TextBlock.TextProperty, nameof(_vm.BudgetLimitLabel));
        var limitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        limitRow.Children.Add(limitLabel);
        limitRow.Children.Add(limit);
        limitRow.Children.Add(limitDisplay);
        editor.Children.Add(limitRow);

        var note = new TextBox { Watermark = "Note (optional)" };
        BindTo(note, TextBox.TextProperty, nameof(_vm.BudgetNote));
        editor.Children.Add(note);

        var saveBtn = new Button { Content = "Save budget", Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)), Foreground = Brushes.White };
        BindTo(saveBtn, Button.CommandProperty, nameof(_vm.SaveBudgetCommand));
        var delBtn = new Button { Content = "Delete" };
        BindTo(delBtn, Button.CommandProperty, nameof(_vm.DeleteBudgetCommand));
        var newBtn = new Button { Content = "New" };
        newBtn.Click += (_, _) => _vm.ResetBudgetEditor();
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionsRow.Children.Add(saveBtn);
        actionsRow.Children.Add(delBtn);
        actionsRow.Children.Add(newBtn);
        editor.Children.Add(actionsRow);

        var exportRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var exportBudgets = new Button { Content = "Export budgets" };
        BindTo(exportBudgets, Button.CommandProperty, nameof(_vm.ExportBudgetsCommand));
        var exportVariance = new Button { Content = "Export variance" };
        BindTo(exportVariance, Button.CommandProperty, nameof(_vm.ExportBudgetVarianceCommand));
        exportRow.Children.Add(exportBudgets);
        exportRow.Children.Add(exportVariance);
        editor.Children.Add(exportRow);

        var right = new Grid { RowDefinitions = new RowDefinitions("*,*"), Margin = new Thickness(0) };
        var budgetsGrid = NewGrid(nameof(_vm.Budgets));
        budgetsGrid.DoubleTapped += BudgetsGrid_DoubleTapped;
        Grid.SetRow(budgetsGrid, 0);
        var varianceIntro = new TextBlock { Text = "Budget variance per month", FontWeight = FontWeight.Bold, Opacity = 0.8 };
        var varianceGrid = NewGrid(nameof(_vm.BudgetVariance));
        var variancePanel = new StackPanel { Spacing = 4, Margin = new Thickness(0,0,0,0) };
        variancePanel.Children.Add(varianceIntro);
        variancePanel.Children.Add(varianceGrid);
        Grid.SetRow(variancePanel, 1);
        right.Children.Add(budgetsGrid);
        right.Children.Add(variancePanel);
        Grid.SetColumn(right, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            Margin = new Thickness(8),
            Children = { editor, right }
        };
    }

    private void BudgetsGrid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is BudgetRow row)
            _vm.BeginEditBudget(row);
    }

    private DataGrid NewGrid(string itemsPath)
    {
        var grid = new DataGrid { AutoGenerateColumns = true, IsReadOnly = true, MinColumnWidth = 90 };
        BindTo(grid, ItemsControl.ItemsSourceProperty, itemsPath);
        return grid;
    }

    private static Border Card(Control child) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFA)),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14),
        Child = child
    };

    private static void BindTo(AvaloniaObject target, AvaloniaProperty property, string path)
        => target[!property] = new Binding(path);

    private async void BrowseImport_Click(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a statement file",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("CSV files") { Patterns = new[] { "*.csv" } },
                new("OFX files") { Patterns = new[] { "*.ofx", "*.qfx" } },
                new("All files") { Patterns = new[] { "*.*" } }
            }
        });
        if (files.Count > 0)
            _vm.SetImportFilePath(files[0].Path.LocalPath);
    }

    private void RulesGrid_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is RuleRow row)
            _vm.BeginEditRule(row);
    }
}