using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace AlphaNative.Services;

public static class PdfPrintService
{
    public static bool PrintToPdf(FlowDocument document, string title, Window owner)
    {
        MessageBox.Show(owner,
            "接下来会打开 Windows 打印窗口。请选择“Microsoft Print to PDF”，然后指定 PDF 保存位置。",
            "导出 PDF", MessageBoxButton.OK, MessageBoxImage.Information);

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return false;

        var oldPageHeight = document.PageHeight;
        var oldPageWidth = document.PageWidth;
        var oldPagePadding = document.PagePadding;
        var oldColumnWidth = document.ColumnWidth;

        try
        {
            document.PageWidth = dialog.PrintableAreaWidth;
            document.PageHeight = dialog.PrintableAreaHeight;
            document.PagePadding = new Thickness(44, 38, 44, 50);
            document.ColumnWidth = double.PositiveInfinity;
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(paginator, title);
            return true;
        }
        finally
        {
            document.PageHeight = oldPageHeight;
            document.PageWidth = oldPageWidth;
            document.PagePadding = oldPagePadding;
            document.ColumnWidth = oldColumnWidth;
        }
    }
}
