namespace TradeX.Tests.Blazor;

public class ExchangesPageMarkupTests
{
    [Fact]
    public async Task ExchangesPage_HistoryButton_OpensOrdersDialog()
    {
        var markup = await File.ReadAllTextAsync(GetExchangesPagePath());

        Assert.Contains("<FluentDialog @ref=\"ordersDialog\" Class=\"orders-dialog\" Modal=\"true\">", markup);
        Assert.Contains("OnClick=\"@(() => LoadOrdersAsync(account.Id, account.Label, OrderListType.History))\"", markup);
        Assert.Contains("await ordersDialog.ShowAsync();", markup);
        Assert.Contains("ordersError", markup);
        Assert.DoesNotContain("showOrders", markup);
    }

    [Fact]
    public async Task ExchangesPage_OrdersDialog_ProvidesLoadingEmptyErrorAndCloseStates()
    {
        var markup = await File.ReadAllTextAsync(GetExchangesPagePath());

        Assert.Contains("<FluentSpinner /> 加载中...", markup);
        Assert.Contains("暂无历史订单", markup);
        Assert.Contains("role=\"alert\">@ordersError</div>", markup);
        Assert.Contains("OnClick=\"CloseOrdersAsync\">关闭</FluentButton>", markup);
        Assert.Contains("await ordersDialog.HideAsync();", markup);
    }

    [Fact]
    public async Task ExchangesPage_TestConnection_UsesInformationMessage()
    {
        var markup = await File.ReadAllTextAsync(GetExchangesPagePath());

        Assert.Contains("ShowInfo(\"连接测试成功\")", markup);
        Assert.Contains("ShowInfo($\"连接测试未通过", markup);
        Assert.Contains("ShowInfo($\"连接测试请求失败", markup);
        Assert.DoesNotContain("ShowError($\"连接失败", markup);
        Assert.DoesNotContain("ShowError($\"请求失败", markup);
    }

    private static string GetExchangesPagePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "TradeX.Blazor",
                "Components",
                "Pages",
                "Exchanges.razor");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Unable to locate Exchanges.razor.");
    }
}
