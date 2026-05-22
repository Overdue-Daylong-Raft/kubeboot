using Pulumi;
using K8s = Pulumi.Kubernetes;
using K8sHelm = Pulumi.Kubernetes.Helm.V3;
using K8sInputs = Pulumi.Kubernetes.Types.Inputs;

internal sealed class TailscaleOperatorComponentArgs
{
    public required string ClusterName { get; init; }
    public required string TailnetDnsDomain { get; init; }
    public required K8s.Provider KubernetesProvider { get; init; }
    public required Input<string> OAuthClientId { get; init; }
    public required Input<string> OAuthClientSecret { get; init; }
    public required string OperatorTag { get; init; }
    public required string ProxyTag { get; init; }
}

internal sealed class TailscaleOperatorComponent : ComponentResource
{
    public K8sHelm.Release Release { get; }
    public string SharedIngressProxyGroupName { get; } = "shared-ingress-proxies";
    public Output<string> OperatorFqdn { get; }

    public TailscaleOperatorComponent(string name, TailscaleOperatorComponentArgs args, ComponentResourceOptions? options = null)
        : base("kubeboot:components:TailscaleOperator", name, options)
    {
        var operatorHostname = $"{args.ClusterName}-k8s-operator";

        Release = new K8sHelm.Release($"{name}-chart", new()
        {
            Name = "tailscale-operator",
            Chart = "tailscale-operator",
            Namespace = "tailscale",
            CreateNamespace = true,
            RepositoryOpts = new K8sInputs.Helm.V3.RepositoryOptsArgs
            {
                Repo = "https://pkgs.tailscale.com/helmcharts",
            },
            Values = new InputMap<object>
            {
                ["oauth"] = new Dictionary<string, object>
                {
                    ["clientId"] = args.OAuthClientId,
                    ["clientSecret"] = args.OAuthClientSecret,
                },
                ["operatorConfig"] = new Dictionary<string, object>
                {
                    ["hostname"] = operatorHostname,
                    ["defaultTags"] = new[] { args.OperatorTag },
                },
                ["apiServerProxyConfig"] = new Dictionary<string, object>
                {
                    ["mode"] = "true",
                    ["allowImpersonation"] = "true",
                },
            },
            Timeout = 900,
        }, ChildOptions());

        var proxyClass = new K8s.ApiExtensions.CustomResource($"{name}-shared-proxyclass", new ProxyClassArgs
        {
            Metadata = new K8sInputs.Meta.V1.ObjectMetaArgs
            {
                Name = "shared-ingress-proxyclass",
            },
            Spec = new InputMap<object>
            {
                ["statefulSet"] = new Dictionary<string, object>
                {
                    ["pod"] = new Dictionary<string, object>
                    {
                        ["tailscaleContainer"] = new Dictionary<string, object>
                        {
                            ["env"] = new[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["name"] = "TS_EXPERIMENTAL_SERVICE_AUTO_ADVERTISEMENT",
                                    ["value"] = "true",
                                },
                            },
                        },
                    },
                },
            },
        }, ChildOptions(new CustomResourceOptions
        {
            DependsOn = { Release },
            Provider = args.KubernetesProvider,
        }));

        _ = new K8s.ApiExtensions.CustomResource($"{name}-shared-proxygroup", new ProxyGroupArgs
        {
            Metadata = new K8sInputs.Meta.V1.ObjectMetaArgs
            {
                Name = SharedIngressProxyGroupName,
            },
            Spec = new InputMap<object>
            {
                ["type"] = "ingress",
                ["replicas"] = 3,
                ["tags"] = new[] { args.ProxyTag },
                ["proxyClass"] = "shared-ingress-proxyclass",
            },
        }, ChildOptions(new CustomResourceOptions
        {
            DependsOn = { proxyClass },
            Provider = args.KubernetesProvider,
        }));

        OperatorFqdn = Output.Create($"{operatorHostname}.{args.TailnetDnsDomain}");

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["operatorFqdn"] = OperatorFqdn,
            ["sharedIngressProxyGroupName"] = SharedIngressProxyGroupName,
        });
    }

    private CustomResourceOptions ChildOptions(CustomResourceOptions? options = null)
    {
        options ??= new CustomResourceOptions();
        options.Parent = this;
        return options;
    }
}
