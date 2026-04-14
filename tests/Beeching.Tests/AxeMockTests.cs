using Beeching.Commands;
using Beeching.Helpers;
using Beeching.Models;
using Moq;
using System.Net;

namespace Beeching.Tests;

public class AxeMockTests
{
    private static AxeContext CreateContext(
        bool force = false,
        bool debug = false,
        int maxRetries = 3,
        int retryPause = 0)
    {
        var settings = new AxeSettings
        {
            Force = force,
            Debug = debug,
            MaxRetries = maxRetries,
            RetryPause = retryPause,
        };
        return new AxeContext(settings)
        {
            SubscriptionRole = "Owner",
            IsSubscriptionRolePrivileged = true,
        };
    }

    private static Resource CreateResource(
        string name = "test-resource",
        string apiVersion = "2023-01-01",
        bool isLocked = false,
        List<ResourceLock>? locks = null)
    {
        var resource = new Resource
        {
            Id = $"/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/{name}",
            Name = name,
            Type = "Microsoft.Compute/virtualMachines",
            ApiVersion = apiVersion,
            OutputMessage = name,
            IsLocked = isLocked,
        };

        if (locks != null)
        {
            resource.ResourceLocks.AddRange(locks);
        }

        return resource;
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content = "")
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content),
        };
    }

    #region SwingTheAxe Tests

    [Fact]
    public async Task SwingTheAxe_SuccessfulDelete_ReturnsEmptyAxeList()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.True(result.Status);
        Assert.Empty(result.AxeList);
    }

    [Fact]
    public async Task SwingTheAxe_MultipleResources_DeletesAll()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource>
        {
            CreateResource("vm1"),
            CreateResource("vm2"),
            CreateResource("vm3"),
        };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.True(result.Status);
        Assert.Empty(result.AxeList);
        mockClient.Verify(c => c.DeleteAsync(It.IsAny<string>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SwingTheAxe_DeleteFails_WithUnknownError_AddsToRetryList()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "Something went wrong"));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.False(result.Status);
        Assert.Single(result.AxeList);
        Assert.Equal("vm1", result.AxeList[0].Name);
    }

    [Fact]
    public async Task SwingTheAxe_Forbidden_SkipsResourceNotRetried()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.Forbidden));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.False(result.Status);
        Assert.Empty(result.AxeList); // Forbidden is not retried
    }

    [Fact]
    public async Task SwingTheAxe_NotFound_SkipsResourceNotRetried()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.NotFound));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.False(result.Status);
        Assert.Empty(result.AxeList); // 404 is not retried
    }

    [Fact]
    public async Task SwingTheAxe_LockedError_SkipsResourceNotRetried()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.Conflict, "Please remove the lock and try again"));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.False(result.Status);
        Assert.Empty(result.AxeList); // Lock error is not retried
    }

    [Fact]
    public async Task SwingTheAxe_MixedResults_OnlyFailedResourceRetried()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.Is<string>(u => u.Contains("vm1"))))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));
        mockClient
            .Setup(c => c.DeleteAsync(It.Is<string>(u => u.Contains("vm2"))))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError, "error"));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource>
        {
            CreateResource("vm1"),
            CreateResource("vm2"),
        };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.False(result.Status);
        Assert.Single(result.AxeList);
        Assert.Equal("vm2", result.AxeList[0].Name);
    }

    [Fact]
    public async Task SwingTheAxe_LockedResourceWithForce_RemovesLockThenDeletes()
    {
        var mockClient = new Mock<IArmClient>();
        // Lock removal succeeds
        mockClient
            .Setup(c => c.DeleteAsync(It.Is<string>(u => u.Contains("/locks/"))))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));
        // Resource deletion succeeds
        mockClient
            .Setup(c => c.DeleteAsync(It.Is<string>(u => u.Contains("virtualMachines"))))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(force: true);
        var locks = new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" }
        };
        var resources = new List<Resource> { CreateResource("vm1", isLocked: true, locks: locks) };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.True(result.Status);
        Assert.Empty(result.AxeList);
        mockClient.Verify(c => c.DeleteAsync(It.Is<string>(u => u.Contains("/locks/"))), Times.Once);
    }

    [Fact]
    public async Task SwingTheAxe_LockedResourceWithForce_LockRemovalFails_SkipsResource()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.Forbidden));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(force: true, maxRetries: 1);
        var locks = new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" }
        };
        var resources = new List<Resource> { CreateResource("vm1", isLocked: true, locks: locks) };

        var result = await axe.SwingTheAxe(context, resources);

        Assert.False(result.Status);
        // Resource deletion should never be attempted
        mockClient.Verify(
            c => c.DeleteAsync(It.Is<string>(u => u.Contains("virtualMachines"))),
            Times.Never);
    }

    #endregion

    #region TryRemoveLocks Tests

    [Fact]
    public async Task TryRemoveLocks_SingleLock_Success_ReturnsTrue()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resource = CreateResource("vm1", isLocked: true, locks: new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" }
        });

        var result = await axe.TryRemoveLocks(context, resource);

        Assert.True(result);
    }

    [Fact]
    public async Task TryRemoveLocks_MultipleLocks_AllSucceed_ReturnsTrue()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resource = CreateResource("vm1", isLocked: true, locks: new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" },
            new() { Id = "/locks/lock2", Name = "lock2", Scope = "resource group" },
        });

        var result = await axe.TryRemoveLocks(context, resource);

        Assert.True(result);
        mockClient.Verify(c => c.DeleteAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TryRemoveLocks_FirstLockFails_StillAttemptsSecondLock()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.Is<string>(u => u.Contains("lock1"))))
            .ReturnsAsync(CreateResponse(HttpStatusCode.Forbidden));
        mockClient
            .Setup(c => c.DeleteAsync(It.Is<string>(u => u.Contains("lock2"))))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(maxRetries: 1);
        var resource = CreateResource("vm1", isLocked: true, locks: new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" },
            new() { Id = "/locks/lock2", Name = "lock2", Scope = "resource group" },
        });

        var result = await axe.TryRemoveLocks(context, resource);

        Assert.False(result);
        // Verify both locks were attempted
        mockClient.Verify(c => c.DeleteAsync(It.Is<string>(u => u.Contains("lock1"))), Times.Once);
        mockClient.Verify(c => c.DeleteAsync(It.Is<string>(u => u.Contains("lock2"))), Times.Once);
    }

    [Fact]
    public async Task TryRemoveLocks_RetriesOnFailure()
    {
        int callCount = 0;
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount < 3
                    ? CreateResponse(HttpStatusCode.InternalServerError)
                    : CreateResponse(HttpStatusCode.OK);
            });

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(maxRetries: 5, retryPause: 0);
        var resource = CreateResource("vm1", isLocked: true, locks: new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" }
        });

        var result = await axe.TryRemoveLocks(context, resource);

        Assert.True(result);
        Assert.Equal(3, callCount); // Failed twice, succeeded on third
    }

    [Fact]
    public async Task TryRemoveLocks_ExhaustsRetries_ReturnsFalse()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.InternalServerError));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(maxRetries: 2, retryPause: 0);
        var resource = CreateResource("vm1", isLocked: true, locks: new List<ResourceLock>
        {
            new() { Id = "/locks/lock1", Name = "lock1", Scope = "resource" }
        });

        var result = await axe.TryRemoveLocks(context, resource);

        Assert.False(result);
        mockClient.Verify(c => c.DeleteAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    #endregion

    #region HandleDeleteFailure Tests

    [Fact]
    public async Task HandleDeleteFailure_LockedMessage_SetsStatusFalse_DoesNotRetry()
    {
        var response = CreateResponse(HttpStatusCode.Conflict, "Please remove the lock and try again");
        var resource = CreateResource("vm1");
        var axeStatus = new AxeStatus();

        await Axe.HandleDeleteFailure(response, resource, axeStatus);

        Assert.False(axeStatus.Status);
        Assert.Empty(axeStatus.AxeList);
    }

    [Fact]
    public async Task HandleDeleteFailure_Forbidden_SetsStatusFalse_DoesNotRetry()
    {
        var response = CreateResponse(HttpStatusCode.Forbidden);
        var resource = CreateResource("vm1");
        var axeStatus = new AxeStatus();

        await Axe.HandleDeleteFailure(response, resource, axeStatus);

        Assert.False(axeStatus.Status);
        Assert.Empty(axeStatus.AxeList);
    }

    [Fact]
    public async Task HandleDeleteFailure_NotFound_SetsStatusFalse_DoesNotRetry()
    {
        var response = CreateResponse(HttpStatusCode.NotFound);
        var resource = CreateResource("vm1");
        var axeStatus = new AxeStatus();

        await Axe.HandleDeleteFailure(response, resource, axeStatus);

        Assert.False(axeStatus.Status);
        Assert.Empty(axeStatus.AxeList);
    }

    [Fact]
    public async Task HandleDeleteFailure_UnknownError_AddsToAxeListForRetry()
    {
        var response = CreateResponse(HttpStatusCode.InternalServerError, "error");
        var resource = CreateResource("vm1");
        var axeStatus = new AxeStatus();

        await Axe.HandleDeleteFailure(response, resource, axeStatus);

        Assert.False(axeStatus.Status);
        Assert.Single(axeStatus.AxeList);
        Assert.Equal("vm1", axeStatus.AxeList[0].Name);
    }

    #endregion

    #region ExecuteAxeWithRetries Tests

    [Fact]
    public async Task ExecuteAxeWithRetries_AllSucceed_Returns0()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.ExecuteAxeWithRetries(context, resources);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ExecuteAxeWithRetries_PermanentFailure_Returns1()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.Forbidden));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext();
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.ExecuteAxeWithRetries(context, resources);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task ExecuteAxeWithRetries_TransientFailureThenSuccess_Returns0()
    {
        int callCount = 0;
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? CreateResponse(HttpStatusCode.InternalServerError, "transient")
                    : CreateResponse(HttpStatusCode.OK);
            });

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(maxRetries: 3, retryPause: 0);
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.ExecuteAxeWithRetries(context, resources);

        Assert.Equal(0, result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAxeWithRetries_ExhaustsRetries_Returns1()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .Returns(() => Task.FromResult(CreateResponse(HttpStatusCode.InternalServerError, "persistent error")));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(maxRetries: 2, retryPause: 0);
        var resources = new List<Resource> { CreateResource("vm1") };

        var result = await axe.ExecuteAxeWithRetries(context, resources);

        Assert.Equal(1, result);
        // maxRetries=2 means the loop runs at retryCount 1 and 2 = 2 total delete calls
        mockClient.Verify(c => c.DeleteAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    #endregion

    #region ReapplyLocks Tests

    [Fact]
    public async Task SwingTheAxe_SuccessfulDelete_ReappliesSubscriptionLock()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));
        mockClient
            .Setup(c => c.PutAsync(It.IsAny<string>(), It.IsAny<HttpContent>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(force: true);
        var locks = new List<ResourceLock>
        {
            new() { Id = "/locks/sub-lock", Name = "sub-lock", Scope = "subscription" }
        };
        var resources = new List<Resource> { CreateResource("vm1", isLocked: true, locks: locks) };

        await axe.SwingTheAxe(context, resources);

        // Verify lock was reapplied via PUT
        mockClient.Verify(
            c => c.PutAsync(It.Is<string>(u => u.Contains("/locks/sub-lock")), It.IsAny<HttpContent>()),
            Times.Once);
    }

    [Fact]
    public async Task SwingTheAxe_SuccessfulDelete_DoesNotReapplyResourceLock()
    {
        var mockClient = new Mock<IArmClient>();
        mockClient
            .Setup(c => c.DeleteAsync(It.IsAny<string>()))
            .ReturnsAsync(CreateResponse(HttpStatusCode.OK));

        var axe = new Axe(mockClient.Object);
        var context = CreateContext(force: true);
        var locks = new List<ResourceLock>
        {
            new() { Id = "/locks/res-lock", Name = "res-lock", Scope = "resource" }
        };
        var resources = new List<Resource> { CreateResource("vm1", isLocked: true, locks: locks) };

        await axe.SwingTheAxe(context, resources);

        // Resource-scoped locks should NOT be reapplied (the resource is deleted)
        mockClient.Verify(
            c => c.PutAsync(It.IsAny<string>(), It.IsAny<HttpContent>()),
            Times.Never);
    }

    #endregion
}
