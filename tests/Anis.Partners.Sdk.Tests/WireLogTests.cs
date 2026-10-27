using Anis.Partners.Sample;
using System.Text;

namespace Anis.Partners.Sdk.Tests;

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;

[Collection("Console")]
public sealed class WireLogTests
{
    [Theory]
    [InlineData("POST", "/v1/wallets/71825266-7182-4182-8182-718252667182/direct-recharges")]
    [InlineData("PATCH", "/v1/direct-recharges/7d4e1a90-5b2c-4e7f-8a13-0c9d6e2b4f81/recipient")]
    [InlineData("POST", "/v1/wallets/71825266-7182-4182-8182-718252667182/meter-charges")]
    public async Task Product_wire_logs_redact_private_bodies_without_changing_the_request(string method, string path)
    {
        using var output = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(output);
            using var client = new HttpClient(new WireLog(dryRun: true, forwardedFor: null));
            using var request = new HttpRequestMessage(new HttpMethod(method), "https://partners.anis.ly" + path);
            const string Body = """{"recipient":[{"key":"recipient_number","value":"01012345678"}],"note":"private note","meterNumber":"0268000710262"}""";
            request.Content = new StringContent(Body, Encoding.UTF8, "application/json");
            request.Headers.Add("Signature", "sig1=:private-signature:");
            await Assert.ThrowsAsync<DryRunException>(() => client.SendAsync(request, TestContext.Current.CancellationToken));
            Assert.Equal(Body, await request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var logged = output.ToString();
            Assert.Contains("<redacted product body>", logged, StringComparison.Ordinal);
            foreach (var secret in new[] { "01012345678", "recipient_number", "private note", "0268000710262", "private-signature" })
                Assert.DoesNotContain(secret, logged, StringComparison.Ordinal);
        }
        finally { Console.SetOut(original); }
    }
}
