namespace Avtomagazin.MauiShared;

public static class ShellTabs
{
    public static TabBar Create(params (string Title, Page Page)[] items)
    {
        var bar = new TabBar();
        foreach (var item in items)
        {
            bar.Items.Add(new ShellContent
            {
                Title = item.Title,
                Content = item.Page
            });
        }

        return bar;
    }
}
