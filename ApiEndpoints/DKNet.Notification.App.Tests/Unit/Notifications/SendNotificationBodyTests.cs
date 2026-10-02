using System.Text;
using DKNet.Notification.Api.ApiEndpoints.Notifications;
using Microsoft.AspNetCore.Http;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>DRK-2013 step 3: the body is read with a 64 KB limit, and a body that cannot be read is no failure.</summary>
public sealed class SendNotificationBodyTests
{
    private const string Email = """{"channel":"email","templateId":"account-opened","parameters":{"to":"jane@example.com"}}""";

    private static async Task<SendNotificationBody> Bind(string body, int chunk = int.MaxValue)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider(),
            Request = { Body = new ChunkedStream(Encoding.UTF8.GetBytes(body), chunk) }
        };

        return (await SendNotificationBody.BindAsync(context)).ShouldNotBeNull();
    }

    private static string Padded(int bytes) => Email + new string(' ', bytes - Encoding.UTF8.GetByteCount(Email));

    [Fact]
    public async Task A_JSON_body_is_read()
    {
        var body = await Bind(Email);

        body.IsTooLarge.ShouldBeFalse();
        var request = body.Request.ShouldNotBeNull();
        request.Channel.ShouldBe("email");
        request.TemplateId.ShouldBe("account-opened");
        request.Parameters.ShouldBe(new Dictionary<string, string> { ["to"] = "jane@example.com" });
    }

    [Fact]
    public async Task A_body_of_exactly_64_KB_read_in_small_chunks_is_read()
    {
        var body = await Bind(Padded(65_536), chunk: 1000);

        body.IsTooLarge.ShouldBeFalse();
        body.Request.ShouldNotBeNull().TemplateId.ShouldBe("account-opened");
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1000)]
    public async Task A_body_of_65537_bytes_is_too_large(int chunk)
    {
        var body = await Bind(Padded(65_537), chunk);

        body.IsTooLarge.ShouldBeTrue();
        body.Request.ShouldBeNull();
    }

    [Theory]
    [InlineData(65_537)]
    [InlineData(2_000_000)]
    public async Task A_declared_size_over_64_KB_is_too_large_without_a_read(long declared)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider(),
            Request = { Body = new UnreadableStream(), ContentLength = declared }
        };

        var body = (await SendNotificationBody.BindAsync(context)).ShouldNotBeNull();

        body.IsTooLarge.ShouldBeTrue();
        body.Request.ShouldBeNull();
    }

    [Fact]
    public async Task A_declared_size_of_exactly_64_KB_is_read()
    {
        var bytes = Encoding.UTF8.GetBytes(Padded(65_536));
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddOptions().BuildServiceProvider(),
            Request = { Body = new MemoryStream(bytes), ContentLength = bytes.Length }
        };

        var body = (await SendNotificationBody.BindAsync(context)).ShouldNotBeNull();

        body.IsTooLarge.ShouldBeFalse();
        body.Request.ShouldNotBeNull().TemplateId.ShouldBe("account-opened");
    }

    [Theory]
    [InlineData("this is not JSON")]
    [InlineData("")]
    [InlineData("""{"channel":"email","templateId":"account-opened","parameters":{"amount":100}}""")]
    [InlineData("""{"channel":"email","templateId":"account-opened","parameters":{"to":["jane@example.com"]}}""")]
    public async Task A_body_that_is_not_JSON_of_the_call_shape_has_no_request(string json)
    {
        var body = await Bind(json);

        body.IsTooLarge.ShouldBeFalse();
        body.Request.ShouldBeNull();
    }

    [Fact]
    public async Task A_field_left_out_arrives_as_null()
    {
        var request = (await Bind("""{"channel":"email","templateId":"account-opened"}""")).Request.ShouldNotBeNull();

        request.Parameters.ShouldBeNull();
    }

    /// <summary>A body stream that fails on read, as the host does past its own size limit.</summary>
    private sealed class UnreadableStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default) =>
            throw new IOException("The body is larger than the host allows.");
    }

    /// <summary>A body stream that hands out at most <paramref name="chunk" /> bytes per read, as a network does.</summary>
    private sealed class ChunkedStream(byte[] buffer, int chunk) : MemoryStream(buffer)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default) =>
            base.ReadAsync(destination[..Math.Min(chunk, destination.Length)], cancellationToken);
    }
}
