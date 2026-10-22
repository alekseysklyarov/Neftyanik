using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Web.Imports;
using Xunit;
using static Neftyanik.Portal.Web.Imports.YearWorkbook;

namespace Neftyanik.Portal.Web.Tests;

public sealed class YearTransferPageTests
{
    private static string Url(string kind, string mode) => $"/neftyanik/Administration/YearTransfer/{kind}/{mode}";
    private static string Input(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + name + "\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Expected input " + name); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Theory]
    [InlineData(RoleNames.Administrator, true)]
    [InlineData(RoleNames.Accountant, true)]
    [InlineData(RoleNames.Member, false)]
    public async Task FourSeparateOperationsRespectRoles(string role, bool allowed)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("year-" + role, role), cultureName: "ru-RU");
        foreach (var kind in new[] { Payments, Readings })
        foreach (var mode in new[] { "import", "export" })
        {
            var response = await client.GetAsync(Url(kind, mode) + "?Year=2025");
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
            if (allowed) Assert.Contains("2025", await response.Content.ReadAsStringAsync());
        }
        if (allowed)
        {
            var dashboard = await client.GetStringAsync("/neftyanik/Administration");
            foreach (var kind in new[] { Payments, Readings })
            foreach (var mode in new[] { "import", "export" }) Assert.Contains($"/Administration/YearTransfer/{kind}/{mode}", dashboard);
            Assert.Contains("id=\"transferYear\"", dashboard);
        }
    }

    [Theory]
    [InlineData(Payments)]
    [InlineData(Readings)]
    public async Task UploadPreviewConfirmAndExportThroughHttp(string kind)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(db => YearTransferTests.Seed(db));
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("year-http-admin", RoleNames.Administrator), cultureName: "ru-RU");
        var url = Url(kind, "import");
        var html = await client.GetStringAsync(url + "?Year=2026");
        var csrf = Input(html, "__RequestVerificationToken");
        var template = await client.GetAsync(url + "?handler=Template&Year=2026"); Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        using var upload = new MultipartFormDataContent
        {
            { new StringContent(csrf), "__RequestVerificationToken" }, { new StringContent("2026"), "Year" },
            { new ByteArrayContent(Write(kind, 2026, [kind == Payments ? YearTransferTests.Payment() : YearTransferTests.Reading()])), "Upload", "year.xlsx" }
        };
        var preview = await client.PostAsync(url + "?handler=Preview", upload); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        html = await preview.Content.ReadAsStringAsync(); var token = Input(html, "PreviewToken"); csrf = Input(html, "__RequestVerificationToken");
        await factory.ExecuteDbContextAsync(async db => { Assert.Equal(0, await db.Payments.CountAsync()); Assert.Equal(0, await db.MemberElectricityReadings.CountAsync()); });
        Dictionary<string, string> Confirm(string year = "2026", string? value = null) => new()
            { ["__RequestVerificationToken"] = csrf, ["Year"] = year, ["PreviewToken"] = value ?? token };
        var wrongYear = await client.PostAsync(url + "?handler=Confirm", new FormUrlEncodedContent(Confirm("2025"))); Assert.Equal(HttpStatusCode.OK, wrongYear.StatusCode);
        await factory.ExecuteDbContextAsync(async db => { Assert.Equal(0, await db.Payments.CountAsync()); Assert.Equal(0, await db.MemberElectricityReadings.CountAsync()); });
        var tamper = await client.PostAsync(url + "?handler=Confirm", new FormUrlEncodedContent(Confirm(value: "tampered"))); Assert.Equal(HttpStatusCode.OK, tamper.StatusCode);
        var confirm = await client.PostAsync(url + "?handler=Confirm", new FormUrlEncodedContent(Confirm())); Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        var repeat = await client.PostAsync(url + "?handler=Confirm", new FormUrlEncodedContent(Confirm())); Assert.Equal(HttpStatusCode.Redirect, repeat.StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            if (kind == Payments) Assert.Equal(1, await db.Payments.CountAsync());
            else Assert.Equal(1, await db.MemberElectricityReadings.CountAsync());
        });
        var exportUrl = Url(kind, "export"); html = await client.GetStringAsync(exportUrl + "?Year=2026");
        var export = await client.PostAsync(exportUrl + "?handler=Download", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = Input(html, "__RequestVerificationToken"), ["Year"] = "2026" }));
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Contains("2026", export.Content.Headers.ContentDisposition!.FileName!);
        var data = Read(await export.Content.ReadAsByteArrayAsync(), kind, 2026); Assert.Empty(data.Issues); Assert.Single(data.Rows);
        var noCsrf = await client.PostAsync(url + "?handler=Confirm", new FormUrlEncodedContent(new Dictionary<string, string> { ["PreviewToken"] = token, ["Year"] = "2026" }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task InvalidYearIsShownWithoutDownloadingAFile()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("year-invalid", RoleNames.Administrator), cultureName: "en-US");
        var response = await client.GetAsync(Url(Payments, "import") + "?Year=1999&handler=Template");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("2000", await response.Content.ReadAsStringAsync());
    }
}
