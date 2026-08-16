using System.Text.RegularExpressions;
using LocalMediaService.Web.Options;
using Microsoft.Extensions.Options;

namespace LocalMediaService.Web.Services;

public sealed partial class StreamingServiceCatalog
{
    private readonly IReadOnlyList<StreamingServiceDefinition> _services;
    private readonly IReadOnlySet<string> _ids;

    public StreamingServiceCatalog(IOptions<StreamingServicesOptions> options)
    {
        _services = options.Value.Services.AsReadOnly();
        _ids = _services.Select(service => service.Id).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<StreamingServiceDefinition> Services => _services;

    public bool Contains(string id) => _ids.Contains(id);

    public static bool IsValid(IReadOnlyCollection<StreamingServiceDefinition>? services)
    {
        if (services is null || services.Count == 0)
        {
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in services)
        {
            if (!ServiceIdRegex().IsMatch(service.Id) ||
                string.IsNullOrWhiteSpace(service.Name) ||
                !IsHttpsUrl(service.HomeUrl) ||
                !IsHttpsUrl(service.LoginUrl) ||
                !ids.Add(service.Id))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHttpsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceIdRegex();
}
