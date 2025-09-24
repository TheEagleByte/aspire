#pragma warning disable ASPIRECOMPUTE001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using static Aspire.Hosting.Utils.AzureManifestUtils;

namespace Aspire.Hosting.Azure.Tests;

public class TargetComputeResourceAnnotationTests
{
    [Fact]
    public async Task CanAccessComputeResourceAnnotationsFromContainerAppCallback()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);

        builder.AddAzureContainerAppEnvironment("env");

        // Add custom annotation to the compute resource to test accessing it from callback
        var customAnnotation = new CustomTestAnnotation("test-value");
        var callbackExecuted = false;
        string? annotationValue = null;

        // This simulates a developer using PublishAsAzureContainerApp and accessing compute resource annotations
        builder.AddContainer("api", "myimage")
            .WithAnnotation(customAnnotation)
            .PublishAsAzureContainerApp((infrastructure, containerApp) =>
            {
                callbackExecuted = true;

                // Access the original compute resource through the back-pointer annotation
                if (infrastructure.AspireResource.TryGetLastAnnotation<TargetComputeResourceAnnotation>(out var backPointer))
                {
                    // Now we can access annotations from the original compute resource
                    if (backPointer.ComputeResource.TryGetLastAnnotation<CustomTestAnnotation>(out var annotation))
                    {
                        annotationValue = annotation.Value;
                    }
                }
            });

        using var app = builder.Build();
        await ExecuteBeforeStartHooksAsync(app, default);

        // Get the provisioning resource and check if the callback was invoked
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var container = Assert.Single(model.GetContainerResources());
        container.TryGetLastAnnotation<DeploymentTargetAnnotation>(out var target);
        var provisioningResource = target?.DeploymentTarget as AzureProvisioningResource;
        Assert.NotNull(provisioningResource);

        // Invoke the bicep generation to trigger the callback
        var (manifest, bicep) = await GetManifestWithBicep(provisioningResource);

        Assert.True(callbackExecuted);
        Assert.Equal("test-value", annotationValue);
    }

    [Fact]
    public async Task CanAccessComputeResourceAnnotationsFromAppServiceCallback()
    {
        using var builder = TestDistributedApplicationBuilder.Create(DistributedApplicationOperation.Publish);

        builder.AddAzureAppServiceEnvironment("env");

        // Add custom annotation to the compute resource to test accessing it from callback
        var customAnnotation = new CustomTestAnnotation("app-service-value");
        var callbackExecuted = false;
        string? annotationValue = null;

        // This simulates a developer using PublishAsAzureAppServiceWebsite and accessing compute resource annotations
        var projectBuilder = builder.AddProject<Project>("api", launchProfileName: null)
            .WithHttpEndpoint()
            .WithExternalHttpEndpoints()
            .WithAnnotation(customAnnotation)
            .PublishAsAzureAppServiceWebsite((infrastructure, website) =>
            {
                callbackExecuted = true;

                // Access the original compute resource through the back-pointer annotation
                if (infrastructure.AspireResource.TryGetLastAnnotation<TargetComputeResourceAnnotation>(out var backPointer))
                {
                    // Now we can access annotations from the original compute resource
                    if (backPointer.ComputeResource.TryGetLastAnnotation<CustomTestAnnotation>(out var annotation))
                    {
                        annotationValue = annotation.Value;
                    }
                }
            });

        using var app = builder.Build();
        await ExecuteBeforeStartHooksAsync(app, default);

        // Get the provisioning resource and check if the callback was invoked
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var project = Assert.IsType<IComputeResource>(Assert.Single(model.GetProjectResources()), exactMatch: false);
        var target = project.GetDeploymentTargetAnnotation();
        var provisioningResource = target?.DeploymentTarget as AzureProvisioningResource;
        Assert.NotNull(provisioningResource);

        // Invoke the bicep generation to trigger the callback
        var (manifest, bicep) = await GetManifestWithBicep(provisioningResource);

        Assert.True(callbackExecuted);
        Assert.Equal("app-service-value", annotationValue);
    }

    [Fact]
    public void TargetComputeResourceAnnotationCanBeCreatedWithResource()
    {
        var mockResource = new TestResource("test");
        var annotation = new TargetComputeResourceAnnotation(mockResource);

        Assert.NotNull(annotation.ComputeResource);
        Assert.Same(mockResource, annotation.ComputeResource);
    }

    [Fact]
    public void TargetComputeResourceAnnotationCanBeRetrievedFromAzureProvisioningResource()
    {
        var mockResource = new TestResource("test");
        var provisioningResource = new AzureProvisioningResource("test-provisioning", _ => { });

        // Add the annotation
        provisioningResource.Annotations.Add(new TargetComputeResourceAnnotation(mockResource));

        // Retrieve it
        Assert.True(provisioningResource.TryGetLastAnnotation<TargetComputeResourceAnnotation>(out var retrievedAnnotation));
        Assert.NotNull(retrievedAnnotation);
        Assert.Same(mockResource, retrievedAnnotation.ComputeResource);
    }

    private sealed class Project : IProjectMetadata
    {
        public string ProjectPath => "test";
        public LaunchSettings LaunchSettings => new();
    }
}

// Test annotation class to simulate custom annotations on compute resources
internal sealed class CustomTestAnnotation(string value) : IResourceAnnotation
{
    public string Value { get; } = value;
}

// Simple test resource implementation for unit testing
internal sealed class TestResource(string name) : Resource(name)
{
}