using Hangfire;
using Xunit;
using Xunit.Abstractions;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

/// <summary>
/// Verifies that the reflection logic used in RecurringJobTriggerService
/// will work correctly with the actual Hangfire API.
///
/// This test protects against breaking changes in Hangfire that would
/// cause the reflection-based enqueuing to fail.
/// </summary>
public class HangfireReflectionVerificationTest
{
    private readonly ITestOutputHelper _output;

    public HangfireReflectionVerificationTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BackgroundJob_HasEnqueueMethodWithExpressionParameter()
    {
        // This test verifies that BackgroundJob static class has the Enqueue<T> method
        // that takes Expression<Func<T, Task>> parameter, which is what we use in RecurringJobTriggerService

        // Find the generic Enqueue<T> method that takes Expression<Func<T, Task>>
        var enqueueMethod = typeof(BackgroundJob)
            .GetMethods()
            .Where(m => m.Name == "Enqueue" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                var parameters = m.GetParameters();
                if (parameters.Length != 1) return false;

                var paramType = parameters[0].ParameterType;
                if (!paramType.IsGenericType) return false;

                var genericTypeDef = paramType.GetGenericTypeDefinition();
                return genericTypeDef == typeof(System.Linq.Expressions.Expression<>);
            });

        // Log details for debugging
        _output.WriteLine("=== VERIFICATION RESULTS ===");

        if (enqueueMethod != null)
        {
            _output.WriteLine($"✅ Found method: {enqueueMethod}");
            _output.WriteLine($"   Return type: {enqueueMethod.ReturnType}");

            var param = enqueueMethod.GetParameters()[0];
            _output.WriteLine($"   Parameter: {param.Name} ({param.ParameterType})");
        }
        else
        {
            _output.WriteLine("❌ Method not found!");

            // Log all Enqueue methods for debugging
            var allEnqueueMethods = typeof(BackgroundJob)
                .GetMethods()
                .Where(m => m.Name == "Enqueue")
                .ToList();

            _output.WriteLine($"\nFound {allEnqueueMethods.Count} Enqueue methods:");
            foreach (var method in allEnqueueMethods)
            {
                _output.WriteLine($"  - {method}");
                _output.WriteLine($"    IsGeneric: {method.IsGenericMethodDefinition}");
                _output.WriteLine($"    Parameters: {method.GetParameters().Length}");

                foreach (var p in method.GetParameters())
                {
                    _output.WriteLine($"      {p.Name}: {p.ParameterType}");
                }
            }
        }

        // Assert that we found the method
        Assert.NotNull(enqueueMethod);

        // Verify return type is string (job ID)
        Assert.Equal(typeof(string), enqueueMethod.ReturnType);
    }

    [Fact]
    public void CanCreateGenericEnqueueMethodForConcreteType()
    {
        // This test verifies that we can successfully make a generic method
        // for a concrete type, which is what happens at runtime

        var enqueueMethod = typeof(BackgroundJob)
            .GetMethods()
            .Where(m => m.Name == "Enqueue" && m.IsGenericMethodDefinition)
            .FirstOrDefault(m =>
            {
                var parameters = m.GetParameters();
                if (parameters.Length != 1) return false;

                var paramType = parameters[0].ParameterType;
                if (!paramType.IsGenericType) return false;

                var genericTypeDef = paramType.GetGenericTypeDefinition();
                return genericTypeDef == typeof(System.Linq.Expressions.Expression<>);
            });

        Assert.NotNull(enqueueMethod);

        // Create a concrete version for a test type
        var testJobType = typeof(TestRecurringJob);
        var concreteMethod = enqueueMethod.MakeGenericMethod(testJobType);

        Assert.NotNull(concreteMethod);

        _output.WriteLine($"✅ Successfully created generic method for {testJobType.Name}");
        _output.WriteLine($"   Concrete method: {concreteMethod}");
    }

    [Fact]
    public void CanCreateExpressionTreeForJobExecution()
    {
        // This test verifies that we can create the expression tree
        // that represents job => job.ExecuteAsync(cancellationToken)

        var jobType = typeof(TestRecurringJob);
        var executeMethod = typeof(Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJob)
            .GetMethod(nameof(Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJob.ExecuteAsync));

        Assert.NotNull(executeMethod);

        // Create the expression tree
        var parameter = System.Linq.Expressions.Expression.Parameter(jobType, "job");
        var methodCall = System.Linq.Expressions.Expression.Call(
            parameter,
            executeMethod,
            System.Linq.Expressions.Expression.Default(typeof(CancellationToken))
        );
        var lambda = System.Linq.Expressions.Expression.Lambda(methodCall, parameter);

        Assert.NotNull(lambda);

        _output.WriteLine($"✅ Successfully created expression tree");
        _output.WriteLine($"   Lambda type: {lambda.Type}");
        _output.WriteLine($"   Body: {lambda.Body}");

        // Verify the lambda type is compatible with Expression<Func<TestRecurringJob, Task>>
        var expectedFuncType = typeof(Func<,>).MakeGenericType(jobType, typeof(Task));
        var expectedLambdaType = typeof(System.Linq.Expressions.Expression<>).MakeGenericType(expectedFuncType);

        // The actual type will be Expression1<Func<T, Task>> which inherits from Expression<Func<T, Task>>
        Assert.True(expectedLambdaType.IsAssignableFrom(lambda.GetType()),
            $"Expected lambda type to be assignable to {expectedLambdaType}, but got {lambda.GetType()}");
    }

    /// <summary>
    /// Test recurring job class for verification
    /// </summary>
    private class TestRecurringJob : Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJob
    {
        public Anela.Heblo.Domain.Features.BackgroundJobs.RecurringJobMetadata Metadata { get; }

        public TestRecurringJob()
        {
            Metadata = new Anela.Heblo.Domain.Features.BackgroundJobs.RecurringJobMetadata
            {
                JobName = "test-job",
                DisplayName = "Test Job",
                Description = "Test job for verification",
                CronExpression = "0 0 * * *"
            };
        }

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
