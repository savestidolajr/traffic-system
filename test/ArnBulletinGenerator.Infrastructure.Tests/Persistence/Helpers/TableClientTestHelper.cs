using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;

/// <summary>
///     Helpers for injecting a mock <see cref="TableClient" /> into repositories
///     that extend <see cref="TableRepositoryBase" />.
/// </summary>
internal static class TableClientTestHelper
{
    /// <summary>
    ///     Replaces the <see cref="TableRepositoryBase.TableClient" /> auto-property
    ///     backing field with the supplied mock and marks the table as ensured.
    /// </summary>
    internal static void InjectMockTableClient(object repository, TableClient mockClient)
    {
        var baseType = typeof(TableRepositoryBase);

        var clientField = baseType.GetField("<TableClient>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        clientField!.SetValue(repository, mockClient);

        var ensuredField = baseType.GetField("_tableEnsured",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ensuredField!.SetValue(repository, true);
    }

    internal static IOptions<StorageOptions> StubOptions()
    {
        return Options.Create(new StorageOptions { AccountName = "" });
    }

    internal static TokenCredential StubCredential()
    {
        return Substitute.For<TokenCredential>();
    }

    /// <summary>Creates an <see cref="AsyncPageable{T}" /> from a list of entities.</summary>
    internal static AsyncPageable<T> ToAsyncPageable<T>(IEnumerable<T> items) where T : notnull
    {
        var list = items.ToList();
        var page = Page<T>.FromValues(list, null, Substitute.For<Response>());
        return AsyncPageable<T>.FromPages([page]);
    }

    /// <summary>Creates an empty <see cref="AsyncPageable{T}" />.</summary>
    internal static AsyncPageable<T> EmptyPageable<T>() where T : notnull
    {
        return ToAsyncPageable<T>([]);
    }

    /// <summary>Creates a <see cref="Response{T}" /> wrapping the given entity value.</summary>
    internal static Response<T> MockResponse<T>(T value)
    {
        return Response.FromValue(value, Substitute.For<Response>());
    }

    /// <summary>Creates a stub <see cref="Response" /> for void operations.</summary>
    internal static Response StubResponse()
    {
        // A real subclass, not an NSubstitute proxy: Response.Headers is virtual, so a proxy
        // returns default(ResponseHeaders) (inner response null) and reading Headers.ETag throws.
        return new StubHttpResponse();
    }

    /// <summary>
    ///     Minimal no-header <see cref="Response" /> mirroring the Azure SDK's MockResponse
    ///     pattern, so header-dependent repository code (e.g. ETag refresh after a write)
    ///     observes a genuinely empty header set rather than an NSubstitute default.
    /// </summary>
    private sealed class StubHttpResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get => Stream.Null; set { } }
        public override string ClientRequestId { get => string.Empty; set { } }
        public override void Dispose() { }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
    }
}
