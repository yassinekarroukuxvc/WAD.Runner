using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WAD.Runner.Api;

public enum JavaDatabaseTarget
{
    Production,
    Test
}

public static class JavaDatabaseTargetParser
{
    public static JavaDatabaseTarget ParseOrDefault(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return JavaDatabaseTarget.Production;

        var value = raw.Trim();

        if (value.Equals("production", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("prod", StringComparison.OrdinalIgnoreCase))
        {
            return JavaDatabaseTarget.Production;
        }

        if (value.Equals("test", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("testing", StringComparison.OrdinalIgnoreCase))
        {
            return JavaDatabaseTarget.Test;
        }

        throw new ArgumentException(
            $"Unsupported database '{raw}'. Supported values: Production, Test.",
            nameof(raw));
    }

    public static bool TryParse(string? raw, out JavaDatabaseTarget target)
    {
        try
        {
            target = ParseOrDefault(raw);
            return true;
        }
        catch (ArgumentException)
        {
            target = JavaDatabaseTarget.Production;
            return false;
        }
    }

    public static string ToWireValue(JavaDatabaseTarget target) =>
        target == JavaDatabaseTarget.Test ? "test" : "production";
}

public sealed class JavaDatabaseSelectionContext
{
    private readonly AsyncLocal<JavaDatabaseTarget?> _current = new();

    public JavaDatabaseTarget Current =>
        _current.Value ?? JavaDatabaseTarget.Production;

    public IDisposable Push(JavaDatabaseTarget target)
    {
        var previous = _current.Value;
        _current.Value = target;
        return new Scope(this, previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly JavaDatabaseSelectionContext _owner;
        private readonly JavaDatabaseTarget? _previous;
        private bool _disposed;

        public Scope(JavaDatabaseSelectionContext owner, JavaDatabaseTarget? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _owner._current.Value = _previous;
        }
    }
}

public sealed class JavaDatabaseSelectionHandler : DelegatingHandler
{
    private const string QueryKey = "database";
    private readonly JavaDatabaseSelectionContext _selection;

    public JavaDatabaseSelectionHandler(JavaDatabaseSelectionContext selection)
    {
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null)
            throw new InvalidOperationException("Java DB API request has no RequestUri.");

        request.RequestUri = AddOrReplaceDatabaseQuery(
            request.RequestUri,
            JavaDatabaseTargetParser.ToWireValue(_selection.Current));

        return base.SendAsync(request, cancellationToken);
    }

    private static Uri AddOrReplaceDatabaseQuery(Uri uri, string database)
    {
        var original = uri.OriginalString;

        var fragmentIndex = original.IndexOf('#');
        var fragment = fragmentIndex >= 0 ? original[fragmentIndex..] : string.Empty;
        var withoutFragment = fragmentIndex >= 0 ? original[..fragmentIndex] : original;

        var queryIndex = withoutFragment.IndexOf('?');
        var path = queryIndex >= 0 ? withoutFragment[..queryIndex] : withoutFragment;
        var query = queryIndex >= 0 ? withoutFragment[(queryIndex + 1)..] : string.Empty;

        var pairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith(QueryKey + "=", StringComparison.OrdinalIgnoreCase))
            .ToList();

        pairs.Add($"{QueryKey}={Uri.EscapeDataString(database)}");

        var rebuilt = $"{path}?{string.Join("&", pairs)}{fragment}";
        return new Uri(rebuilt, uri.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
    }
}