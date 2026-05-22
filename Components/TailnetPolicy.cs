using System.Text.Json;
using System.Text.Json.Nodes;
using Pulumi;
using Tailscale = Pulumi.Tailscale;

internal sealed class TailnetPolicyArgs
{
    public required string ClusterName { get; init; }
    public required string TailnetDnsDomain { get; init; }
    public required string NodeTag { get; init; }
    public required string OperatorTag { get; init; }
    public required string ProxyTag { get; init; }
    public required string ArgoCdServiceTag { get; init; }
    public required IReadOnlyList<string> KubernetesApiAccessSources { get; init; }
    public required IReadOnlyList<string> ArgoCdAccessSources { get; init; }
    public string? ExternalPolicyLink { get; init; }
}

internal sealed class TailnetPolicy : ComponentResource
{
    public Tailscale.TailnetSettings TailnetSettings { get; }
    public Tailscale.Acl AccessPolicy { get; }

    public TailnetPolicy(string name, TailnetPolicyArgs args, ComponentResourceOptions? options = null)
        : base("kubeboot:components:TailnetPolicy", name, options)
    {
        var tailnetSettingsArgs = new Tailscale.TailnetSettingsArgs
        {
            AclsExternallyManagedOn = true,
            HttpsEnabled = true,
        };

        if (!string.IsNullOrWhiteSpace(args.ExternalPolicyLink))
        {
            tailnetSettingsArgs.AclsExternalLink = args.ExternalPolicyLink;
        }

        TailnetSettings = new Tailscale.TailnetSettings($"{name}-settings", tailnetSettingsArgs, ChildOptions());

        AccessPolicy = new Tailscale.Acl($"{name}-acl", new Tailscale.AclArgs
        {
            AclJson = BuildPolicy(args),
            OverwriteExistingContent = true,
        }, ChildOptions());

        RegisterOutputs(new Dictionary<string, object?>
        {
            ["tailnetSettingsId"] = TailnetSettings.Id,
            ["aclId"] = AccessPolicy.Id,
        });
    }

    private static string BuildPolicy(TailnetPolicyArgs args)
    {
        static JsonArray Array(params string[] values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
        static JsonArray NodeArray(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

        var firstKubernetesSource = args.KubernetesApiAccessSources.First();
        var firstArgoCdSource = args.ArgoCdAccessSources.First();

        var policy = new JsonObject
        {
            ["tagOwners"] = new JsonObject
            {
                [args.NodeTag] = Array("autogroup:admin"),
                [args.OperatorTag] = Array(args.NodeTag),
                [args.ProxyTag] = Array(args.OperatorTag),
                [args.ArgoCdServiceTag] = Array(args.OperatorTag),
            },
            ["autoApprovers"] = new JsonObject
            {
                ["services"] = new JsonObject
                {
                    [args.ArgoCdServiceTag] = Array(args.ProxyTag),
                },
            },
            ["grants"] = new JsonArray
            {
                new JsonObject
                {
                    ["src"] = NodeArray(args.KubernetesApiAccessSources),
                    ["dst"] = Array(args.OperatorTag),
                    ["ip"] = Array("tcp:443"),
                    ["app"] = new JsonObject
                    {
                        ["tailscale.com/cap/kubernetes"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["impersonate"] = new JsonObject
                                {
                                    ["groups"] = Array("system:masters"),
                                },
                            },
                        },
                    },
                },
                new JsonObject
                {
                    ["src"] = NodeArray(args.KubernetesApiAccessSources),
                    ["dst"] = Array(args.NodeTag),
                    ["ip"] = Array("tcp:6443"),
                },
                new JsonObject
                {
                    ["src"] = Array(args.NodeTag),
                    ["dst"] = Array(args.NodeTag),
                    ["ip"] = Array("*"),
                },
                new JsonObject
                {
                    ["src"] = NodeArray(args.ArgoCdAccessSources),
                    ["dst"] = Array(args.ArgoCdServiceTag),
                    ["ip"] = Array("tcp:80", "tcp:443"),
                },
                new JsonObject
                {
                    ["src"] = NodeArray(args.ArgoCdAccessSources),
                    ["dst"] = Array($"{args.ProxyTag}:*"),
                    ["ip"] = Array("icmp:*"),
                },
            },
            ["tests"] = new JsonArray
            {
                new JsonObject
                {
                    ["src"] = firstKubernetesSource,
                    ["proto"] = "tcp",
                    ["accept"] = new JsonArray(
                        JsonValue.Create($"{args.OperatorTag}:443"),
                        JsonValue.Create($"{args.NodeTag}:6443")),
                },
                new JsonObject
                {
                    ["src"] = args.NodeTag,
                    ["proto"] = "tcp",
                    ["accept"] = new JsonArray(JsonValue.Create($"{args.NodeTag}:6443")),
                },
                new JsonObject
                {
                    ["src"] = firstArgoCdSource,
                    ["proto"] = "tcp",
                    ["accept"] = new JsonArray(JsonValue.Create("svc:argocd:443")),
                },
                new JsonObject
                {
                    ["src"] = firstArgoCdSource,
                    ["proto"] = "icmp",
                    ["accept"] = new JsonArray(JsonValue.Create($"{args.ProxyTag}:0")),
                },
            },
        };

        return policy.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    private CustomResourceOptions ChildOptions(CustomResourceOptions? options = null)
    {
        options ??= new CustomResourceOptions();
        options.Parent = this;
        return options;
    }
}
