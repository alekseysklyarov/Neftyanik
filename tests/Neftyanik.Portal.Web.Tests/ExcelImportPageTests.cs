using System.Net;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Web.Imports;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class ExcelImportPageTests
{
    private const string Url = "/neftyanik/Administration/Import";

    [Theory]
    [InlineData(RoleNames.Administrator, true)]
    [InlineData(RoleNames.Accountant, false)]
    [InlineData(RoleNames.Member, false)]
    public async Task OnlyAdministratorCanAccessImportAndTemplate(string role, bool allowed)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("import-" + role, role), cultureName: "ru-RU");
        foreach (var url in new[] { Url, Url + "?handler=Template" })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
        }
    }

    [Fact]
    public async Task UploadReviewConfirmAndRepeat_WorkThroughHttpWithAntiforgery()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("excel-admin", RoleNames.Administrator), cultureName: "ru-RU");
        var page = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var csrf = Input(html, "__RequestVerificationToken");
        var template = await client.GetAsync(Url + "?handler=Template");
        using var book = new XLWorkbook(new MemoryStream(await template.Content.ReadAsByteArrayAsync()));
        var sheet = book.Worksheet("Import");
        sheet.Cell(2, 1).Value = "M1"; sheet.Cell(2, 2).Value = "HTTP Imported Owner";
        sheet.Cell(2, 5).Value = "HTTP-001"; sheet.Cell(2, 9).Value = -50;
        using var stream = new MemoryStream(); book.SaveAs(stream);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(csrf), "__RequestVerificationToken" },
            { new StringContent("2026-01-01"), "TransferDate" },
            { new ByteArrayContent(stream.ToArray()), "Upload", "import.xlsx" }
        };
        var preview = await client.PostAsync(Url + "?handler=Preview", form);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        html = await preview.Content.ReadAsStringAsync();
        var token = Input(html, "PreviewToken");
        csrf = Input(html, "__RequestVerificationToken");
        await factory.ExecuteDbContextAsync(async db => Assert.False(await db.Members.AnyAsync(m => m.FullName == "HTTP Imported Owner")));
        var confirm = await client.PostAsync(Url + "?handler=Confirm", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = csrf, ["PreviewToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var owner = await db.Members.SingleAsync(m => m.FullName == "HTTP Imported Owner");
            var payment = await db.Payments.SingleAsync(p => p.MemberId == owner.Id);
            Assert.Equal(PaymentMethod.OpeningBalance, payment.PaymentMethod);
            var receipt = await client.GetAsync($"/neftyanik/Payments/{payment.Id}/Receipt?memberId={owner.Id}");
            Assert.Equal(HttpStatusCode.NotFound, receipt.StatusCode);
        });
        var repeat = await client.PostAsync(Url + "?handler=Confirm", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = csrf, ["PreviewToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        await factory.ExecuteDbContextAsync(async db => Assert.Equal(1, await db.Members.CountAsync(m => m.FullName == "HTTP Imported Owner")));
        var noCsrf = await client.PostAsync(Url + "?handler=Confirm", new FormUrlEncodedContent(new Dictionary<string, string> { ["PreviewToken"] = token }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    private static string Input(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected input " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
