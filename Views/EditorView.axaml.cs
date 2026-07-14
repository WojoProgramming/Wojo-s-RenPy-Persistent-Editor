using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WojoPersistentEditor.Controls;
using WojoPersistentEditor.Models;

namespace WojoPersistentEditor.Views
{
    public partial class EditorView : UserControl
    {
        private readonly List<PersistentVariable> allVariables;
        private readonly List<List<VariablePageItem>> pages;

        private int currentPage;
        private bool isPreviewMode;

        private const int FallbackPanelWidth = 1090;
        private const int AvailablePageHeight = 585;
        private const int MinimumVariableHeight = 67;
        private const int ValueBoxHeightWithMargin = 43;
        private const int CollectionBoxWidthWithMargin = 168;
        private const int LayoutHorizontalSpace = 388;
        private const int LayoutVerticalSpace = 24;

        public EditorView()
        {
            allVariables = new List<PersistentVariable>();
            pages = new List<List<VariablePageItem>>();
            currentPage = 0;
            isPreviewMode = false;

            InitializeComponent();

            UpdatePageControls();
        }

        public void LoadVariables(List<PersistentVariable>? variables)
        {
            allVariables.Clear();

            if (variables != null)
            {
                foreach (PersistentVariable variable in variables)
                {
                    variable.Values ??= new List<PersistentEditorValue>();

                    foreach (
                        PersistentEditorValue value in variable.Values
                    )
                    {
                        value.Text ??= string.Empty;
                        value.OriginalText = value.Text;
                    }

                    allVariables.Add(variable);
                }
            }

            currentPage = 0;

            if (!string.IsNullOrEmpty(Browser.Text))
            {
                Browser.Text = string.Empty;
            }
            else
            {
                BuildPages();
            }
        }

        public void SetPreviewMode(bool previewMode)
        {
            isPreviewMode = previewMode;
            currentPage = 0;
            BuildPages();
        }

        public List<PersistentVariable> GetVariables()
        {
            return allVariables;
        }

        public List<PersistentVariable> GetChangedVariables()
        {
            return allVariables
                .Where(variable => variable.HasChanged())
                .ToList();
        }

        private void BuildPages()
        {
            pages.Clear();

            List<PersistentVariable> filteredVariables =
                GetFilteredVariables();
            List<VariablePageItem> currentPageItems =
                new List<VariablePageItem>();

            int usedHeight = 0;

            foreach (PersistentVariable variable in filteredVariables)
            {
                if (!variable.IsCollection || variable.Values.Count == 0)
                {
                    int variableHeight = MeasureVariableHeight(
                        variable,
                        1
                    );

                    if (
                        currentPageItems.Count > 0 &&
                        usedHeight + variableHeight > AvailablePageHeight
                    )
                    {
                        pages.Add(currentPageItems);
                        currentPageItems = new List<VariablePageItem>();
                        usedHeight = 0;
                    }

                    currentPageItems.Add(
                        new VariablePageItem(
                            variable,
                            0,
                            variable.Values.Count
                        )
                    );
                    usedHeight += variableHeight;
                    continue;
                }

                int startIndex = 0;

                while (startIndex < variable.Values.Count)
                {
                    int remainingHeight =
                        AvailablePageHeight - usedHeight;
                    int valuesThatFit = GetCollectionValuesThatFit(
                        remainingHeight
                    );

                    if (valuesThatFit == 0)
                    {
                        if (currentPageItems.Count > 0)
                        {
                            pages.Add(currentPageItems);
                            currentPageItems =
                                new List<VariablePageItem>();
                            usedHeight = 0;
                            continue;
                        }

                        valuesThatFit = GetValuesPerRow();
                    }

                    int valueCount = Math.Min(
                        valuesThatFit,
                        variable.Values.Count - startIndex
                    );
                    int variableHeight = MeasureVariableHeight(
                        variable,
                        valueCount
                    );

                    currentPageItems.Add(
                        new VariablePageItem(
                            variable,
                            startIndex,
                            valueCount
                        )
                    );
                    usedHeight += variableHeight;
                    startIndex += valueCount;

                    if (startIndex < variable.Values.Count)
                    {
                        pages.Add(currentPageItems);
                        currentPageItems =
                            new List<VariablePageItem>();
                        usedHeight = 0;
                    }
                }
            }

            if (currentPageItems.Count > 0)
            {
                pages.Add(currentPageItems);
            }

            if (pages.Count == 0)
            {
                currentPage = 0;
            }
            else if (currentPage >= pages.Count)
            {
                currentPage = pages.Count - 1;
            }

            ShowCurrentPage();
        }

        private List<PersistentVariable> GetFilteredVariables()
        {
            IEnumerable<PersistentVariable> visibleVariables =
                isPreviewMode
                    ? allVariables
                    : allVariables.Where(variable => variable.IsEditable);

            string searchText = (Browser.Text ?? string.Empty).Trim();

            if (searchText.Length == 0)
            {
                return visibleVariables.ToList();
            }

            return visibleVariables
                .Where(variable =>
                    variable.Name.Contains(
                        searchText,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToList();
        }

        private int MeasureVariableHeight(
            PersistentVariable variable,
            int valueCount
        )
        {
            int valuesPerRow = variable.IsCollection
                ? GetValuesPerRow()
                : 1;
            int visibleValueCount = Math.Max(1, valueCount);
            int rowCount =
                (visibleValueCount + valuesPerRow - 1) / valuesPerRow;

            return Math.Max(
                MinimumVariableHeight,
                LayoutVerticalSpace + rowCount * ValueBoxHeightWithMargin
            );
        }

        private int GetValuesPerRow()
        {
            double measuredWidth = VariablesPanel.Bounds.Width;
            int panelWidth = measuredWidth > 0
                ? (int)measuredWidth
                : FallbackPanelWidth;
            int valuesWidth = Math.Max(
                CollectionBoxWidthWithMargin,
                panelWidth - LayoutHorizontalSpace
            );

            return Math.Max(
                1,
                valuesWidth / CollectionBoxWidthWithMargin
            );
        }

        private int GetCollectionValuesThatFit(int remainingHeight)
        {
            int availableRows =
                (remainingHeight - LayoutVerticalSpace) /
                ValueBoxHeightWithMargin;

            if (availableRows <= 0)
            {
                return 0;
            }

            return availableRows * GetValuesPerRow();
        }

        private VariableEditorControl CreateVariableControl(
            VariablePageItem pageItem
        )
        {
            VariableEditorControl variableControl =
                new VariableEditorControl
                {
                    Height = MeasureVariableHeight(
                        pageItem.Variable,
                        pageItem.ValueCount
                    ),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };

            variableControl.LoadVariable(
                pageItem.Variable,
                pageItem.StartIndex,
                pageItem.ValueCount,
                isPreviewMode
            );

            return variableControl;
        }

        private void ShowCurrentPage()
        {
            VariablesPanel.Children.Clear();

            if (pages.Count > 0)
            {
                foreach (
                    VariablePageItem pageItem in pages[currentPage]
                )
                {
                    VariablesPanel.Children.Add(
                        CreateVariableControl(pageItem)
                    );
                }
            }

            EmptyStateText.IsVisible = pages.Count == 0;
            EmptyStateText.Text = allVariables.Count == 0
                ? "No variables to show."
                : "No variables match your search.";

            UpdatePageControls();
        }

        private void UpdatePageControls()
        {
            if (pages.Count == 0)
            {
                PageNumber.Text = "0 / 0";
                LeftButton.IsEnabled = false;
                RightButton.IsEnabled = false;
                return;
            }

            PageNumber.Text =
                (currentPage + 1) + " / " + pages.Count;
            LeftButton.IsEnabled = currentPage > 0;
            RightButton.IsEnabled = currentPage < pages.Count - 1;
        }

        private void LeftButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            if (currentPage <= 0)
            {
                return;
            }

            currentPage--;
            ShowCurrentPage();
        }

        private void RightButton_Click(
            object? sender,
            RoutedEventArgs e
        )
        {
            if (currentPage >= pages.Count - 1)
            {
                return;
            }

            currentPage++;
            ShowCurrentPage();
        }

        private void Browser_TextChanged(
            object? sender,
            TextChangedEventArgs e
        )
        {
            currentPage = 0;
            BuildPages();
        }

        private sealed class VariablePageItem
        {
            public PersistentVariable Variable { get; }
            public int StartIndex { get; }
            public int ValueCount { get; }

            public VariablePageItem(
                PersistentVariable variable,
                int startIndex,
                int valueCount
            )
            {
                Variable = variable;
                StartIndex = startIndex;
                ValueCount = valueCount;
            }
        }
    }
}
