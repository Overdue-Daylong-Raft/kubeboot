using Pulumi;
using K8s = Pulumi.Kubernetes;
using K8sHelm = Pulumi.Kubernetes.Helm.V3;
using K8sInputs = Pulumi.Kubernetes.Types.Inputs;
using K8sNetworking = Pulumi.Kubernetes.Networking.V1;

internal sealed class ArgoCdBootstrapArgs
{
    public required string ClusterName { get; init; }
    public required string TailnetDnsDomain { get; init; }
    public required K8s.Provider KubernetesProvider { get; init; }
    public required string SharedIngressProxyGroupName { get; init; }
    public required string ArgoCdServiceTag { get; init; }
    public required string HostLabel { get; init; }
    public required string GitOpsRepoUrl { get; init; }
    public required string GitOpsRevision { get; init; }
    public required string GitOpsBootstrapPath { get; init; }
}

internal sealed class ArgoCdBootstrap : ComponentResource
{
    public Output<string> ArgoCdUrl { get; }
    public Output<string> RootApplicationName { get; }

    public ArgoCdBootstrap(string name, ArgoCdBootstrapArgs args, ComponentResourceOptions? options = null)
        : base("kubeboot:components:ArgoCdBootstrap", name, options)
    {
        var argoRelease = new K8sHelm.Release($"{name}-chart", new()
        {
            Name = "argocd",
            Chart = "argo-cd",
            Namespace = "argocd",
            CreateNamespace = true,
            RepositoryOpts = new K8sInputs.Helm.V3.RepositoryOptsArgs
            {
                Repo = "https://argoproj.github.io/argo-helm",
            },
            Values = new InputMap<object>
            {
                ["global"] = new Dictionary<string, object>
                {
                    ["domain"] = $"{args.HostLabel}.{args.TailnetDnsDomain}",
                },
                ["configs"] = new Dictionary<string, object>
                {
                    ["params"] = new Dictionary<string, object>
                    {
                        ["server.insecure"] = "true",
                    },
                },
                ["server"] = new Dictionary<string, object>
                {
                    ["ingress"] = new Dictionary<string, object>
                    {
                        ["enabled"] = false,
                    },
                },
            },
            Timeout = 900,
        }, ChildOptions(new CustomResourceOptions
        {
            Provider = args.KubernetesProvider,
        }));

        var ingress = new K8sNetworking.Ingress($"{name}-ingress", new()
        {
            Metadata = new K8sInputs.Meta.V1.ObjectMetaArgs
            {
                Name = "argocd",
                Namespace = "argocd",
                Annotations =
                {
                    ["tailscale.com/proxy-group"] = args.SharedIngressProxyGroupName,
                    ["tailscale.com/tags"] = args.ArgoCdServiceTag,
                },
            },
            Spec = new K8sInputs.Networking.V1.IngressSpecArgs
            {
                IngressClassName = "tailscale",
                DefaultBackend = new K8sInputs.Networking.V1.IngressBackendArgs
                {
                    Service = new K8sInputs.Networking.V1.IngressServiceBackendArgs
                    {
                        Name = "argocd-server",
                        Port = new K8sInputs.Networking.V1.ServiceBackendPortArgs
                        {
                            Number = 80,
                        },
                    },
                },
                Tls =
                {
                    new K8sInputs.Networking.V1.IngressTLSArgs
                    {
                        Hosts = { args.HostLabel },
                    },
                },
            },
        }, ChildOptions(new CustomResourceOptions
        {
            DependsOn = { argoRelease },
            Provider = args.KubernetesProvider,
        }));

        _ = new K8s.ApiExtensions.CustomResource($"{name}-root-project", new ArgoProjectArgs
        {
            Metadata = new K8sInputs.Meta.V1.ObjectMetaArgs
            {
                Name = "control-root",
                Namespace = "argocd",
            },
            Spec = new InputMap<object>
            {
                ["description"] = "Pulumi-created root project for the control-cluster Argo CD bootstrap.",
                ["sourceRepos"] = new[] { args.GitOpsRepoUrl },
                ["destinations"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["server"] = "https://kubernetes.default.svc",
                        ["namespace"] = "argocd",
                    },
                },
                ["clusterResourceWhitelist"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["group"] = "*",
                        ["kind"] = "*",
                    },
                },
                ["namespaceResourceWhitelist"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["group"] = "*",
                        ["kind"] = "*",
                    },
                },
            },
        }, ChildOptions(new CustomResourceOptions
        {
            DependsOn = { argoRelease },
            Provider = args.KubernetesProvider,
        }));

        var rootApplication = new K8s.ApiExtensions.CustomResource($"{name}-root-app", new ArgoApplicationArgs
        {
            Metadata = new K8sInputs.Meta.V1.ObjectMetaArgs
            {
                Name = "control-root",
                Namespace = "argocd",
                Finalizers = { "resources-finalizer.argocd.argoproj.io" },
            },
            Spec = new InputMap<object>
            {
                ["project"] = "control-root",
                ["source"] = new Dictionary<string, object>
                {
                    ["repoURL"] = args.GitOpsRepoUrl,
                    ["targetRevision"] = args.GitOpsRevision,
                    ["path"] = args.GitOpsBootstrapPath,
                },
                ["destination"] = new Dictionary<string, object>
                {
                    ["server"] = "https://kubernetes.default.svc",
                    ["namespace"] = "argocd",
                },
                ["syncPolicy"] = new Dictionary<string, object>
                {
                    ["automated"] = new Dictionary<string, object>
                    {
                        ["prune"] = true,
                        ["selfHeal"] = true,
                    },
                    ["syncOptions"] = new[]
                    {
                        "CreateNamespace=true",
                        "ServerSideApply=true",
                    },
                },
            },
        }, ChildOptions(new CustomResourceOptions
        {
            DependsOn = { ingress },
            Provider = args.KubernetesProvider,
        }));

        ArgoCdUrl = Output.Create($"https://{args.HostLabel}.{args.TailnetDnsDomain}");
        RootApplicationName = rootApplication.Metadata.Apply(metadata => metadata.Name ?? "control-root");

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["argocdUrl"] = ArgoCdUrl,
            ["rootApplicationName"] = RootApplicationName,
        });
    }

    private CustomResourceOptions ChildOptions(CustomResourceOptions? options = null)
    {
        options ??= new CustomResourceOptions();
        options.Parent = this;
        return options;
    }
}
