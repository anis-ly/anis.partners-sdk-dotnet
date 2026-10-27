namespace Anis.Partners.Sdk.Tests;

/// <summary>Owner answers for the meter and direct recharge suites, shaped as the gateway sends them.</summary>
/// <remarks>The templates use @name@ markers so the JSON keeps its braces untouched.</remarks>
internal static class ProductBodies
{
    public static readonly Guid Wallet = Guid.Parse("71825266-7182-4182-8182-718252667182");
    public static readonly Guid MeterCharge = Guid.Parse("3c1e9b7a-0d42-4f5e-8a61-2b9c7d0e5f13");
    public static readonly Guid Package = Guid.Parse("5f3b8c21-7a0d-4e96-b1c4-2d8e6f9a0b37");
    public static readonly Guid DirectRechargeId = Guid.Parse("7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81");
    public static readonly Guid Product = Guid.Parse("e7c2a9d4-1b3f-4a85-9c60-8d2e4f7b1a93");

    public static string Fill(string template, params (string Name, string Value)[] values)
    {
        var result = template
            .Replace("@wallet@", Wallet.ToString("D"), StringComparison.Ordinal)
            .Replace("@package@", Package.ToString("D"), StringComparison.Ordinal)
            .Replace("@product@", Product.ToString("D"), StringComparison.Ordinal);

        foreach (var (name, value) in values)
            result = result.Replace($"@{name}@", value, StringComparison.Ordinal);

        return result;
    }

    public static string MeterAccepted(Guid? id = null)
        => Fill("""{"operationId":"@id@","operationType":"meterCharge","status":"processing"}""", ("id", (id ?? MeterCharge).ToString("D")));

    public static string DirectRechargeAccepted(Guid? id = null)
        => Fill("""{"operationId":"@id@","operationType":"directRecharge","status":"processing"}""", ("id", (id ?? DirectRechargeId).ToString("D")));

    public static string MeterSucceeded(Guid? id = null) => Fill("""
        {"operationId":"@id@","status":"succeeded","walletId":"@wallet@","meterNumber":"0268000710262",
        "package":{"id":"@package@","name":"باقة 100 دينار","gecolPackageName":"PKG_100"},
        "total":{"amount":"97.500","currency":"LYD"},"personalPrice":{"amount":"100.000","currency":"LYD"},
        "externalReference":"POS-77-INV-1093","createdAt":"2026-10-20T08:12:44Z","completedAt":"2026-10-20T08:13:02Z",
        "receipt":{"receiptNumber":"260020103744","creditedAmount":"88.640","tokens":[
        {"sequence":1,"label":"KCT1","code":"1234 5678 9012 3456 7890","description":"Key change token 1"},
        {"sequence":2,"label":"KCT2","code":"2345 6789 0123 4567 8901","description":"Key change token 2"},
        {"sequence":3,"label":"STS","code":"5821 0034 7719 2265 0418","description":"Credit token"}],
        "taxes":[{"description":"ضريبة الاستهلاك","amount":"5.000"},{"description":"رسوم الخدمة","amount":"3.860"}]}}
        """, ("id", (id ?? MeterCharge).ToString("D")));

    public static string MeterProcessing(string status = "processing") => Fill("""
        {"operationId":"@id@","status":"@status@","walletId":"@wallet@","meterNumber":"0268000710262",
        "package":{"id":"@package@","name":"باقة 100 دينار","gecolPackageName":"PKG_100"},
        "total":{"amount":"97.500","currency":"LYD"},"personalPrice":{"amount":"100.000","currency":"LYD"},
        "createdAt":"2026-10-20T08:12:44Z"}
        """, ("id", MeterCharge.ToString("D")), ("status", status));

    public static string MeterFailed(string code, string refund, bool withAmount) => Fill("""
        {"operationId":"@id@","status":"failed","walletId":"@wallet@","meterNumber":"0268000710262",
        "package":{"id":"@package@","name":"باقة 100 دينار","gecolPackageName":"PKG_100"},
        "total":{"amount":"97.500","currency":"LYD"},"personalPrice":{"amount":"100.000","currency":"LYD"},
        "createdAt":"2026-10-20T08:12:44Z",
        "failure":{"code":"@code@","message":{"ar":"مديونية العداد أكبر من قيمة الباقة","en":"The meter's debt is larger than the package"}},
        "refund":{"status":"@refund@"@amount@}}
        """,
        ("id", MeterCharge.ToString("D")), ("code", code), ("refund", refund),
        ("amount", withAmount ? ",\"amount\":{\"amount\":\"97.500\",\"currency\":\"LYD\"}" : string.Empty));

    public static string DirectRecharge(string status, bool partialRefund = false, Guid? id = null)
    {
        var completed = status == "completed" ? ",\"completedAt\":\"2026-10-20T11:42:10Z\",\"proofUrl\":\"https://example.ly/proof\"" : string.Empty;
        var reason = status is "suspendedDataError" or "rejectedAwaitingRefund" or "rejectedWithRefund" ? ",\"reason\":\"Recipient number rejected\"" : string.Empty;
        var refund = partialRefund
            ? ",\"partialRefund\":{\"refundedPayout\":\"500.000\",\"refundedTotal\":{\"amount\":\"77.500\",\"currency\":\"LYD\"},\"remainingPayout\":\"1000.000\",\"remainingTotal\":{\"amount\":\"160.000\",\"currency\":\"LYD\"}}"
            : string.Empty;

        return Fill("""
        {"operationId":"@id@","status":"@status@","number":30412,"walletId":"@wallet@",
        "product":{"id":"@product@","title":"فودافون كاش مصر"},
        "payout":{"amount":"1500.000","currency":{"name":"جنيه مصري"}},"exchangeRate":"0.155000",
        "price":{"amount":"232.500","currency":"LYD"},"commission":{"amount":"5.000","currency":"LYD"},"total":{"amount":"237.500","currency":"LYD"},
        "recipient":[{"key":"recipient_number","name":{"ar":"رقم المحفظة","en":"Wallet number"},"value":"01012345678"},
        {"key":"recipient_location","name":{"ar":"المدينة","en":"City"},"value":"1a2b","valueName":"القاهرة"}],
        "note":"Family support","externalReference":"POS-77-TR-551","requestedAt":"2026-10-20T09:00:00Z"@tail@}
        """, ("id", (id ?? DirectRechargeId).ToString("D")), ("status", status), ("tail", completed + reason + refund));
    }
}
