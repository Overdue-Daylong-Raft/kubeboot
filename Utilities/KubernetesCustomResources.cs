using Pulumi;
using K8s = Pulumi.Kubernetes;

internal sealed class ProxyGroupArgs : K8s.ApiExtensions.CustomResourceArgs
{
    public ProxyGroupArgs() : base("tailscale.com/v1alpha1", "ProxyGroup")
    {
    }

    public required InputMap<object> Spec { get; init; }
}

internal sealed class ProxyClassArgs : K8s.ApiExtensions.CustomResourceArgs
{
    public ProxyClassArgs() : base("tailscale.com/v1alpha1", "ProxyClass")
    {
    }

    public required InputMap<object> Spec { get; init; }
}

internal sealed class ArgoApplicationArgs : K8s.ApiExtensions.CustomResourceArgs
{
    public ArgoApplicationArgs() : base("argoproj.io/v1alpha1", "Application")
    {
    }

    public required InputMap<object> Spec { get; init; }
}

internal sealed class ArgoProjectArgs : K8s.ApiExtensions.CustomResourceArgs
{
    public ArgoProjectArgs() : base("argoproj.io/v1alpha1", "AppProject")
    {
    }

    public required InputMap<object> Spec { get; init; }
}
