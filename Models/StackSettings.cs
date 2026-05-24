using System.Collections.Generic;
using System.Linq;
using Pulumi;

internal sealed class StackSettings
{
    public required string AzureLocation { get; init; }
    public required string ClusterName { get; init; }
    public required string VmSize { get; init; }
    public required string OsImage { get; init; }
    public required string AdminUsername { get; init; }
    public required int ServerCount { get; init; }
    public required string VnetCidr { get; init; }
    public required string SubnetCidr { get; init; }
    public required string PodCidrs { get; init; }
    public required string ServiceCidrs { get; init; }
    public required string K3sChannel { get; init; }
    public required string TailnetDnsDomain { get; init; }
    public required Output<string> TailscaleAuthKey { get; init; }
    public required string TailscaleNodeTag { get; init; }
    public required Output<string> TailscaleOperatorClientSecret { get; init; }
    public required Input<string> TailscaleOperatorClientId { get; init; }
    public required string TailscaleOperatorTag { get; init; }
    public required string TailscaleProxyTag { get; init; }
    public required string ArgoCdServiceTag { get; init; }
    public required string ArgoCdHost { get; init; }
    public required string GitOpsRepoUrl { get; init; }
    public required string GitOpsRevision { get; init; }
    public required string GitOpsBootstrapPath { get; init; }
    public required string? TailscalePolicyExternalLink { get; init; }
    public required IReadOnlyList<string> TailscaleKubernetesApiAccessSources { get; init; }
    public required IReadOnlyList<string> ArgoCdAccessSources { get; init; }

    public static StackSettings Load()
    {
        var config = new Config();
        var azureConfig = new Config("azure-native");

        var clusterName = config.Get("clusterName") ?? config.Get("vmName") ?? "trashcan";
        var tailnetDnsDomain = NormalizeTailnetDomain(config.Require("tailnetDnsDomain"));
        var tailscaleOperatorClientSecret = config.RequireSecret("tailscaleOperatorClientSecret");

        return new StackSettings
        {
            AzureLocation = azureConfig.Require("location"),
            ClusterName = clusterName,
            VmSize = config.Get("vmSize") ?? "Standard_B2pls_v2",
            OsImage = config.Get("osImage") ?? "Canonical:ubuntu-24_04-lts:server-arm64:latest",
            AdminUsername = config.Get("adminUsername") ?? "pulumiuser",
            ServerCount = config.GetInt32("serverCount") ?? 1,
            VnetCidr = config.Get("vnetCidr") ?? "10.0.0.0/16",
            SubnetCidr = config.Get("subnetCidr") ?? "10.0.1.0/24",
            PodCidrs = config.Get("podCidrs") ?? "10.42.0.0/16,2001:cafe:42::/56",
            ServiceCidrs = config.Get("serviceCidrs") ?? "10.43.0.0/16,2001:cafe:43::/112",
            K3sChannel = config.Get("k3sChannel") ?? "stable",
            TailnetDnsDomain = tailnetDnsDomain,
            TailscaleAuthKey = config.RequireSecret("tailscaleAuthKey"),
            TailscaleNodeTag = config.Get("tailscaleNodeTag") ?? $"tag:{clusterName}-node",
            TailscaleOperatorClientSecret = tailscaleOperatorClientSecret,
            TailscaleOperatorClientId = config.Get("tailscaleOperatorClientId") is { } configuredOperatorClientId
                ? Output.Create(configuredOperatorClientId)
                : tailscaleOperatorClientSecret.Apply(ClusterBootstrap.DeriveTailscaleOAuthClientId),
            TailscaleOperatorTag = config.Get("tailscaleOperatorTag") ?? $"tag:{clusterName}-operator",
            TailscaleProxyTag = config.Get("tailscaleProxyTag") ?? $"tag:{clusterName}-proxy",
            ArgoCdServiceTag = config.Get("argocdServiceTag") ?? $"tag:{clusterName}-service-argocd",
            ArgoCdHost = config.Get("argocdHost") ?? "argocd",
            GitOpsRepoUrl = config.Require("gitopsRepoUrl"),
            GitOpsRevision = config.Get("gitopsRevision") ?? "main",
            GitOpsBootstrapPath = config.Get("gitopsBootstrapPath") ?? "gitops/clusters/control",
            TailscalePolicyExternalLink = config.Get("tailscalePolicyExternalLink") ?? config.Get("gitopsRepoUrl"),
            TailscaleKubernetesApiAccessSources = ParseCsv(config.Get("tailscaleKubernetesApiAccessSrcs"), "autogroup:admin"),
            ArgoCdAccessSources = ParseCsv(config.Get("argocdAccessSrcs"), "autogroup:admin"),
        };
    }

    private static IReadOnlyList<string> ParseCsv(string? rawValue, string defaultValue)
    {
        var resolved = string.IsNullOrWhiteSpace(rawValue) ? defaultValue : rawValue;
        return [.. resolved
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)];
    }

    private static string NormalizeTailnetDomain(string value) => value.Trim().Trim('.');
}
