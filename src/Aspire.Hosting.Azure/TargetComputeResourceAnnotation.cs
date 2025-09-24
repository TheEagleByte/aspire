// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.Azure;

/// <summary>
/// Annotation that stores a back-pointer to the original compute resource for an AzureProvisioningResource.
/// This allows code in PublishAsContainerApp or PublishAsAzureAppServiceWebsite callbacks to access 
/// annotations and properties from the original compute resource.
/// </summary>
/// <remarks>
/// <para>
/// When using <c>PublishAsAzureContainerApp</c> or <c>PublishAsAzureAppServiceWebsite</c>, the callback receives
/// an <see cref="AzureResourceInfrastructure"/> parameter whose AspireResource property points to an 
/// <see cref="AzureProvisioningResource"/>. This annotation provides access to the original compute resource
/// (the container or project resource) that was configured with the callback.
/// </para>
/// <para>
/// Example usage in a callback:
/// <code>
/// builder.AddContainer("mycontainer", "myimage")
///     .WithAnnotation(new MyCustomAnnotation("some value"))
///     .PublishAsAzureContainerApp((infrastructure, containerApp) =>
///     {
///         // Access the original compute resource through the back-pointer
///         if (infrastructure.AspireResource.TryGetLastAnnotation&lt;TargetComputeResourceAnnotation&gt;(out var backPointer))
///         {
///             // Now access annotations from the original compute resource
///             if (backPointer.ComputeResource.TryGetLastAnnotation&lt;MyCustomAnnotation&gt;(out var myAnnotation))
///             {
///                 // Use myAnnotation.Value
///                 containerApp.Template.Scale.MinReplicas = myAnnotation.SomeProperty;
///             }
///         }
///     });
/// </code>
/// </para>
/// </remarks>
public sealed class TargetComputeResourceAnnotation(IResource computeResource) : IResourceAnnotation
{
    /// <summary>
    /// Gets the compute resource associated with this annotation.
    /// </summary>
    public IResource ComputeResource { get; } = computeResource;
}
